using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Application.Services;

public class CollaborativeService
{
    private readonly IUserRatingExtendedRepository _ratingsExt;
    private readonly IUserRatingRepository _ratings;
    private readonly IWatchListRepository _watchList;

    private const int MinCommonMovies = 3;   // мінімум спільних фільмів для порівняння
    private const int TopSimilarUsers = 10;  // беремо топ-10 схожих користувачів
    private const double MinSimilarity = 0.1; // мінімальна схожість
    private const double MinRating = 7.0; // рекомендуємо тільки добре оцінені

    public CollaborativeService(
        IUserRatingExtendedRepository ratingsExt,
        IUserRatingRepository ratings,
        IWatchListRepository watchList)
    {
        _ratingsExt = ratingsExt;
        _ratings = ratings;
        _watchList = watchList;
    }

    public async Task<IEnumerable<Recommendation>> GetRecommendationsAsync(
        Guid userId, int limit = 20)
    {
        // 1. Оцінки поточного користувача
        var myRatings = (await _ratings.GetByUserIdAsync(userId))
            .ToDictionary(r => r.MovieId, r => (double)r.Rating);

        if (myRatings.Count < 10)
            return Enumerable.Empty<Recommendation>();

        // 2. Всі оцінки всіх користувачів
        var allRatings = await _ratingsExt.GetAllGroupedByUserAsync();

        // 3. Знаходимо схожих користувачів через Pearson correlation
        var similarUsers = allRatings
            .Where(kv => kv.Key != userId)
            .Select(kv => new
            {
                UserId = kv.Key,
                Ratings = kv.Value,
                Similarity = PearsonCorrelation(myRatings, kv.Value)
            })
            .Where(x => x.Similarity > MinSimilarity)
            .OrderByDescending(x => x.Similarity)
            .Take(TopSimilarUsers)
            .ToList();

        if (!similarUsers.Any())
            return Enumerable.Empty<Recommendation>();

        // 4. Фільми які я вже бачив — виключаємо
        var watchList = await _watchList.GetByUserIdAsync(userId);
        var seenIds = myRatings.Keys
            .Union(watchList.Where(w => w.Status == "watched").Select(w => w.MovieId))
            .ToHashSet();

        // 5. Зважений рейтинг для нових фільмів
        //score = Σ(similarity × rating) / Σ(|similarity|)
        var numerators = new Dictionary<Guid, double>();
        var denominators = new Dictionary<Guid, double>();

        foreach (var similar in similarUsers)
        {
            foreach (var (movieId, rating) in similar.Ratings)
            {
                if (seenIds.Contains(movieId)) continue;
                if (rating < MinRating) continue;

                numerators[movieId] = numerators.GetValueOrDefault(movieId)
                    + similar.Similarity * rating;
                denominators[movieId] = denominators.GetValueOrDefault(movieId)
                    + Math.Abs(similar.Similarity);
            }
        }

        return numerators
            .Where(kv => denominators.ContainsKey(kv.Key) && denominators[kv.Key] > 0)
            .Select(kv => new
            {
                MovieId = kv.Key,
                Score = kv.Value / denominators[kv.Key]
            })
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .Select(x => new Recommendation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                MovieId = x.MovieId,
                Score = (decimal)Math.Round(x.Score / 10.0, 4),
                Algorithm = "collaborative",
                GeneratedAt = DateTime.UtcNow,
                WasClicked = false
            });
    }

    // ── Pearson Correlation ───────────────────────────────────

    private static double PearsonCorrelation(
        Dictionary<Guid, double> a,
        Dictionary<Guid, double> b)
    {
        var common = a.Keys.Intersect(b.Keys).ToList();
        if (common.Count < MinCommonMovies) return 0;

        var avgA = common.Average(k => a[k]);
        var avgB = common.Average(k => b[k]);

        var numerator = common.Sum(k => (a[k] - avgA) * (b[k] - avgB));
        var denA = Math.Sqrt(common.Sum(k => Math.Pow(a[k] - avgA, 2)));
        var denB = Math.Sqrt(common.Sum(k => Math.Pow(b[k] - avgB, 2)));

        return (denA == 0 || denB == 0) ? 0 : numerator / (denA * denB);
    }
}