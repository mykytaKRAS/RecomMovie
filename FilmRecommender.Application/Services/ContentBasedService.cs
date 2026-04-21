using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Application.Services;

public class ContentBasedService
{
    private readonly IMovieScoringRepository _scoring;
    private readonly ISurveyRepository _survey;
    private readonly IUserRatingRepository _ratings;
    private readonly IWatchListRepository _watchList;

    public ContentBasedService(
        IMovieScoringRepository scoring,
        ISurveyRepository survey,
        IUserRatingRepository ratings,
        IWatchListRepository watchList)
    {
        _scoring = scoring;
        _survey = survey;
        _ratings = ratings;
        _watchList = watchList;
    }

    public async Task<IEnumerable<Recommendation>> GetRecommendationsAsync(
        Guid userId, int limit = 20)
    {
        // 1. Будуємо профіль користувача
        var userVector = await BuildUserVectorAsync(userId);

        if (!userVector.Any())
            return Enumerable.Empty<Recommendation>();

        // 2. Збираємо id фільмів які юзер вже бачив
        var seenIds = await GetSeenMovieIdsAsync(userId);

        // 3. Отримуємо кандидатів для scoring
        var candidates = await _scoring.GetAllForScoringAsync(seenIds, limit: 1000);

        // 4. Рахуємо косинусну подібність і сортуємо
        var scored = candidates
            .Where(m => m.FeatureVector != null)
            .Select(m => new
            {
                Movie = m,
                Score = CosineSimilarity(userVector, m.FeatureVector!)
            })
            .Where(x => x.Score > 0.1) // мінімальний поріг релевантності
            .OrderByDescending(x => x.Score)
            .Take(limit)
            .ToList();

        return scored.Select(x => new Recommendation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MovieId = x.Movie.Id,
            Score = (decimal)Math.Round(x.Score, 4),
            Algorithm = "content_based",
            GeneratedAt = DateTime.UtcNow,
            WasClicked = false
        });
    }

    // ── Будуємо вектор профілю користувача ───────────────────

    private async Task<Dictionary<string, double>> BuildUserVectorAsync(Guid userId)
    {
        var vector = new Dictionary<string, double>();

        // З опитувальника (вага 40%)
        var survey = await _survey.GetByUserIdAsync(userId);
        if (survey != null)
        {
            foreach (var (key, val) in survey.PreferenceVector)
            {
                // Ключі в опитувальнику — це genreId, конвертуємо у genre_name
                vector[$"survey_{key}"] = (double)val * 0.4;
            }
        }

        // З оцінок фільмів (вага 60%) — тільки фільми з оцінкою 6+
        var ratings = (await _ratings.GetByUserIdAsync(userId))
            .Where(r => r.Rating >= 6)
            .ToList();

        foreach (var rating in ratings)
        {
            var movieVector = await _scoring.GetFeatureVectorAsync(rating.MovieId);
            if (movieVector is null) continue;

            var weight = (double)rating.Rating / 10.0 * 0.6;

            foreach (var (key, val) in movieVector)
            {
                vector[key] = vector.GetValueOrDefault(key) + val * weight;
            }
        }

        return Normalize(vector);
    }

    private async Task<HashSet<Guid>> GetSeenMovieIdsAsync(Guid userId)
    {
        var rated = await _ratings.GetByUserIdAsync(userId);
        var watchList = await _watchList.GetByUserIdAsync(userId);

        var ids = rated.Select(r => r.MovieId)
            .Union(watchList
                .Where(w => w.Status == "watched")
                .Select(w => w.MovieId));

        return ids.ToHashSet();
    }

    private static double CosineSimilarity(
        Dictionary<string, double> a,
        Dictionary<string, double> b)
    {
        var commonKeys = a.Keys.Intersect(b.Keys).ToList();
        if (!commonKeys.Any()) return 0;

        var dot = commonKeys.Sum(k => a[k] * b[k]);
        var magA = Math.Sqrt(a.Values.Sum(v => v * v));
        var magB = Math.Sqrt(b.Values.Sum(v => v * v));

        return (magA == 0 || magB == 0) ? 0 : dot / (magA * magB);
    }

    private static Dictionary<string, double> Normalize(Dictionary<string, double> v)
    {
        var mag = Math.Sqrt(v.Values.Sum(x => x * x));
        return mag == 0
            ? v
            : v.ToDictionary(k => k.Key, k => k.Value / mag);
    }
}