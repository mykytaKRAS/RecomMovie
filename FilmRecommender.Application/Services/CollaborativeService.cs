// FilmRecommender.Application/Services/CollaborativeService.cs

using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Application.Services;

public class CollaborativeService
{
    private readonly IUserRatingExtendedRepository _ratingsExt;
    private readonly IUserRatingRepository _ratings;
    private readonly IWatchListRepository _watchList;

    private const int MinCommonMovies = 25;
    private const int TopSimilarUsers = 50;
    private const double MinSimilarity = 0.2;
    private const double MinRating = 0.0;

    public CollaborativeService(
        IUserRatingExtendedRepository ratingsExt,
        IUserRatingRepository ratings,
        IWatchListRepository watchList)
    {
        _ratingsExt = ratingsExt;
        _ratings = ratings;
        _watchList = watchList;
    }

    // ══════════════════════════════════════════════════════════
    // PRODUCTION метод — використовується фронтендом
    // ══════════════════════════════════════════════════════════

    public async Task<IEnumerable<Recommendation>> GetRecommendationsAsync(
        Guid userId, int limit = 20)
    {
        var myRatings = (await _ratings.GetByUserIdAsync(userId))
            .ToDictionary(r => r.MovieId, r => (double)r.Rating);

        if (myRatings.Count < 10)
            return Enumerable.Empty<Recommendation>();

        var allRatings = await _ratingsExt.GetAllGroupedByUserAsync();

        var watchList = await _watchList.GetByUserIdAsync(userId);
        var seenIds = myRatings.Keys
            .Union(watchList.Where(w => w.Status == "watched").Select(w => w.MovieId))
            .ToHashSet();

        return ComputeRecommendations(userId, myRatings, allRatings, seenIds, limit);
    }

    // ══════════════════════════════════════════════════════════
    // EVALUATION метод — використовується тільки EvaluationService
    // trainRatings і allRatings передаються ззовні
    // excludeIds включає і train і test → кандидати лише нові фільми
    // Повертає predicted scores для ВСІХ фільмів (не тільки топ)
    // щоб EvaluationService міг знайти test фільми у результатах
    // ══════════════════════════════════════════════════════════

    public IEnumerable<Recommendation> GetRecommendationsForEval(
        Guid userId,
        Dictionary<Guid, double> trainRatings,
        Dictionary<Guid, Dictionary<Guid, double>> allRatings,
        HashSet<Guid> excludeOnlyTrainIds,
        int limit = 1000)
    {
        if (trainRatings.Count < 5)
            return Enumerable.Empty<Recommendation>();

        // Для eval виключаємо тільки train — test залишаємо як кандидати
        return ComputeRecommendations(
            userId, trainRatings, allRatings, excludeOnlyTrainIds, limit);
    }

    // ══════════════════════════════════════════════════════════
    // SHARED — спільна логіка
    // ══════════════════════════════════════════════════════════

    private IEnumerable<Recommendation> ComputeRecommendations(
    Guid userId,
    Dictionary<Guid, double> myRatings,
    Dictionary<Guid, Dictionary<Guid, double>> allRatings,
    HashSet<Guid> excludeIds,
    int limit)
    {
        // 1. Рахуємо середню оцінку цільового користувача (щоб потім додати відхилення)
        var myAvgRating = myRatings.Values.Any() ? myRatings.Values.Average() : 0;

        // Знаходимо схожих користувачів через Pearson
        var similarUsers = allRatings
            .Where(kv => kv.Key != userId)
            .Select(kv => new
            {
                UserId = kv.Key,
                Ratings = kv.Value,
                Similarity = PearsonCorrelation(myRatings, kv.Value),
                // Одразу рахуємо середню оцінку для кожного сусіда
                AvgRating = kv.Value.Values.Average()
            })
            .Where(x => x.Similarity > MinSimilarity)
            .OrderByDescending(x => x.Similarity)
            .Take(TopSimilarUsers)
            .ToList();

        if (!similarUsers.Any())
            return Enumerable.Empty<Recommendation>();

        var numerators = new Dictionary<Guid, double>();
        var denominators = new Dictionary<Guid, double>();

        foreach (var similar in similarUsers)
        {
            foreach (var (movieId, rating) in similar.Ratings)
            {
                if (excludeIds.Contains(movieId)) continue;

                // 2. Рахуємо ВІДХИЛЕННЯ від середнього замість самої оцінки
                var deviation = rating - similar.AvgRating;

                numerators[movieId] = numerators.GetValueOrDefault(movieId)
                    + similar.Similarity * deviation;

                denominators[movieId] = denominators.GetValueOrDefault(movieId)
                    + Math.Abs(similar.Similarity);
            }
        }

        return numerators
            .Where(kv => denominators.ContainsKey(kv.Key) && denominators[kv.Key] > 0)
            .Select(kv =>
            {
                // 3. Зважене відхилення
                var weightedDeviation = kv.Value / denominators[kv.Key];

                // 4. Фінальний прогноз: Середня оцінка цільового користувача + відхилення
                var rawScore = myAvgRating + weightedDeviation;

                // 5. Захист від аномалій: обмежуємо оцінку в межах 1..10
                rawScore = Math.Max(1.0, Math.Min(10.0, rawScore));

                var normalizedScore = rawScore / 10.0;

                return new Recommendation
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    MovieId = kv.Key,
                    Score = (decimal)Math.Round(normalizedScore, 4),
                    Algorithm = "collaborative",
                    GeneratedAt = DateTime.UtcNow,
                    WasClicked = false
                };
            })
            .OrderByDescending(r => r.Score)
            .Take(limit);
    }

    private static double PearsonCorrelation(
        Dictionary<Guid, double> a,
        Dictionary<Guid, double> b)
    {
        var common = a.Keys.Intersect(b.Keys).ToList();
        if (common.Count < MinCommonMovies) return 0;

        var avgA = common.Average(k => a[k]);
        var avgB = common.Average(k => b[k]);

        var num = common.Sum(k => (a[k] - avgA) * (b[k] - avgB));
        var denA = Math.Sqrt(common.Sum(k => Math.Pow(a[k] - avgA, 2)));
        var denB = Math.Sqrt(common.Sum(k => Math.Pow(b[k] - avgB, 2)));

        return (denA == 0 || denB == 0) ? 0 : num / (denA * denB);
    }
}