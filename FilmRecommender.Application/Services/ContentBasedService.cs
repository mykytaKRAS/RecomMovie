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

    // PRODUCTION метод

    public async Task<IEnumerable<Recommendation>> GetRecommendationsAsync(
        Guid userId, int limit = 20)
    {
        var userRatings = (await _ratings.GetByUserIdAsync(userId))
            .ToDictionary(r => r.MovieId, r => (double)r.Rating);

        var userVector = await BuildUserVectorAsync(userId, userRatings);

        if (!userVector.Any())
            return Enumerable.Empty<Recommendation>();

        var seenIds = await GetSeenIdsAsync(userId);
        return await ScoreAndRankAsync(userId, userVector, seenIds, limit);
    }

    // EVALUATION метод

    public async Task<IEnumerable<Recommendation>> GetRecommendationsForEvalAsync(
        Guid userId,
        Dictionary<Guid, double> trainRatings,
        HashSet<Guid> excludeIds,
        int limit = 20)
    {
        var userVector = await BuildUserVectorAsync(userId, trainRatings);

        if (!userVector.Any())
            return Enumerable.Empty<Recommendation>();

        return await ScoreAndRankAsync(userId, userVector, excludeIds, limit);
    }

    // SHARED логіка 

    private async Task<IEnumerable<Recommendation>> ScoreAndRankAsync(
        Guid userId,
        Dictionary<string, double> userVector,
        HashSet<Guid> excludeIds,
        int limit)
    {
        var candidates = await _scoring.GetAllForScoringAsync(excludeIds, 1000);

        var scored = candidates
            .Where(m => m.FeatureVector != null)
            .Select(m => new
            {
                Movie = m,
                Score = CosineSimilarity(userVector, m.FeatureVector!)
            })
            .Where(x => x.Score > 0.1)
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

    private async Task<Dictionary<string, double>> BuildUserVectorAsync(
    Guid userId,
    Dictionary<Guid, double> ratings)
    {
        var vector = new Dictionary<string, double>();

        // Дані з опитувальника (вага 40%)
        var survey = await _survey.GetByUserIdAsync(userId);
        if (survey != null)
        {
            foreach (var (key, val) in survey.PreferenceVector)
            {
                vector[key] = (double)val * 0.4;
            }
        }

        // Дані з оцінок (вага 60%)
        foreach (var (movieId, rating) in ratings)
        {
            var movieVector = await _scoring.GetFeatureVectorAsync(movieId);
            if (movieVector is null) continue;

            //var weight = ((rating - 5.5) / 4.5) * 0.6;
            var weight = (rating / 10.0) * 0.6;

            foreach (var (key, val) in movieVector)
            {
                vector[key] = vector.GetValueOrDefault(key) + (val * weight);
            }
        }

        return Normalize(vector);
    }

    private async Task<HashSet<Guid>> GetSeenIdsAsync(Guid userId)
    {
        var rated = await _ratings.GetByUserIdAsync(userId);
        var watchList = await _watchList.GetByUserIdAsync(userId);

        return rated.Select(r => r.MovieId)
            .Union(watchList
                .Where(w => w.Status == "watched")
                .Select(w => w.MovieId))
            .ToHashSet();
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