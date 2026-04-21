using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Application.Services;

public record VectorEntry(string Key, double Value);

public record SimilarityStep(
    Guid MovieId,
    string MovieTitle,
    string? PosterPath,
    double Score,
    double DotProduct,
    double MagnitudeA,
    double MagnitudeB,
    List<VectorEntry> CommonKeys,
    List<VectorEntry> MovieVector
);

public record ContentBasedLog(
    List<VectorEntry> SurveyContribution,
    List<VectorEntry> RatingsContribution,
    List<VectorEntry> FinalUserVector,
    double UserVectorMagnitude,
    List<SimilarityStep> TopCandidates,
    List<SimilarityStep> FinalRecommendations
);

public record UserSimilarityStep(
    Guid OtherUserId,
    string OtherUsername,
    int CommonMoviesCount,
    List<CommonMovieEntry> CommonMovies,
    double MeanA,
    double MeanB,
    double Numerator,
    double DenominatorA,
    double DenominatorB,
    double Correlation
);

public record CommonMovieEntry(
    Guid MovieId,
    string MovieTitle,
    double RatingA,
    double RatingB,
    double DeviationA,
    double DeviationB,
    double Product
);

public record PredictedScoreStep(
    Guid MovieId,
    string MovieTitle,
    string? PosterPath,
    List<ContributingUser> ContributingUsers,
    double WeightedSum,
    double SimilaritySum,
    double PredictedScore
);

public record ContributingUser(
    Guid UserId,
    string Username,
    double Similarity,
    double Rating,
    double Contribution
);

public record CollaborativeLog(
    int MyRatingCount,
    List<UserSimilarityStep> UserSimilarities,
    List<UserSimilarityStep> TopSimilarUsers,
    List<PredictedScoreStep> PredictedScores,
    List<PredictedScoreStep> FinalRecommendations
);

// ── Сервіс ────────────────────────────────────────────────────

public class RecommendationExplainerService
{
    private readonly IMovieScoringRepository _scoring;
    private readonly ISurveyRepository _survey;
    private readonly IUserRatingRepository _ratings;
    private readonly IUserRatingExtendedRepository _ratingsExt;
    private readonly IWatchListRepository _watchList;
    private readonly IUserRepository _users;
    private readonly IGenreRepository _genres;

    public RecommendationExplainerService(
        IMovieScoringRepository scoring,
        ISurveyRepository survey,
        IUserRatingRepository ratings,
        IUserRatingExtendedRepository ratingsExt,
        IWatchListRepository watchList,
        IUserRepository users,
        IGenreRepository genres)
    {
        _scoring = scoring;
        _survey = survey;
        _ratings = ratings;
        _ratingsExt = ratingsExt;
        _watchList = watchList;
        _users = users;
        _genres = genres;
    }

    // ══════════════════════════════════════════════════════════
    // CONTENT-BASED LOG
    // ══════════════════════════════════════════════════════════

    public async Task<ContentBasedLog> ExplainContentBasedAsync(Guid userId)
    {
        // ── внесок опитувальника ─────────────────────
        var surveyContribution = new List<VectorEntry>();
        var survey = await _survey.GetByUserIdAsync(userId);

        if (survey != null)
        {
            // Завантажуємо всі жанри для маппінгу id → name
            var allGenres = (await _genres.GetAllAsync())
                .ToDictionary(g => g.Id.ToString(), g => g.Name);

            foreach (var (key, val) in survey.PreferenceVector)
            {
                var weighted = (double)val * 0.4;
                if (weighted > 0)
                {
                    // Замінюємо числовий id на назву жанру
                    var label = allGenres.TryGetValue(key, out var name)
                        ? $"genre_{name.ToLower().Replace(" ", "_")}"
                        : $"survey_{key}";

                    surveyContribution.Add(new VectorEntry(label, weighted));
                }
            }
        }

        // ── внесок оцінок ─────────────────────────────
        var ratingsContribution = new List<VectorEntry>();
        var userRatings = (await _ratings.GetByUserIdAsync(userId))
            .Where(r => r.Rating >= 6)
            .ToList();

        var accumulated = new Dictionary<string, double>();

        foreach (var rating in userRatings)
        {
            var movieVector = await _scoring.GetFeatureVectorAsync(rating.MovieId);
            if (movieVector is null) continue;

            var weight = (double)rating.Rating / 10.0 * 0.6;

            foreach (var (key, val) in movieVector)
            {
                accumulated[key] = accumulated.GetValueOrDefault(key) + val * weight;
            }
        }

        foreach (var (key, val) in accumulated.OrderByDescending(x => x.Value).Take(15))
            ratingsContribution.Add(new VectorEntry(key, Math.Round(val, 4)));

        // ── фінальний вектор користувача ─────────────
        var rawVector = new Dictionary<string, double>();

        if (survey != null)
            foreach (var (key, val) in survey.PreferenceVector)
                rawVector[$"survey_{key}"] = (double)val * 0.4;

        foreach (var (key, val) in accumulated)
            rawVector[key] = rawVector.GetValueOrDefault(key) + val;

        var magnitude = Math.Sqrt(rawVector.Values.Sum(v => v * v));
        var normalized = rawVector.ToDictionary(
            k => k.Key,
            k => magnitude > 0 ? Math.Round(k.Value / magnitude, 4) : 0);

        var finalUserVector = normalized
            .OrderByDescending(x => x.Value)
            .Where(x => x.Value > 0.01)
            .Take(20)
            .Select(x => new VectorEntry(x.Key, x.Value))
            .ToList();

        // ── розрахунок схожості з кандидатами ────────
        var seenIds = await GetSeenIdsAsync(userId);
        var candidates = (await _scoring.GetAllForScoringAsync(seenIds, 200)).ToList();

        var similaritySteps = new List<SimilarityStep>();

        foreach (var movie in candidates.Where(m => m.FeatureVector != null).Take(50))
        {
            var (score, dot, magA, magB, commonKeys) =
                CosineSimilarityDetailed(normalized, movie.FeatureVector!);

            similaritySteps.Add(new SimilarityStep(
                movie.Id,
                movie.Title,
                movie.PosterPath,
                Math.Round(score, 4),
                Math.Round(dot, 4),
                Math.Round(magA, 4),
                Math.Round(magB, 4),
                commonKeys
                    .OrderByDescending(k => k.Value)
                    .Take(8)
                    .ToList(),
                movie.FeatureVector!
                    .Where(x => x.Value > 0)
                    .OrderByDescending(x => x.Value)
                    .Take(8)
                    .Select(x => new VectorEntry(x.Key, Math.Round(x.Value, 4)))
                    .ToList()
            ));
        }

        var top = similaritySteps.OrderByDescending(x => x.Score).Take(5).ToList();
        var final = similaritySteps.OrderByDescending(x => x.Score).Take(20).ToList();

        return new ContentBasedLog(
            surveyContribution,
            ratingsContribution,
            finalUserVector,
            Math.Round(magnitude, 4),
            top,
            final
        );
    }

    // COLLABORATIVE LOG

    public async Task<CollaborativeLog> ExplainCollaborativeAsync(Guid userId)
    {
        var myRatings = (await _ratings.GetByUserIdAsync(userId))
            .ToDictionary(r => r.MovieId, r => (double)r.Rating);

        var allRatings = await _ratingsExt.GetAllGroupedByUserAsync();

        // ── розрахунок Pearson для кожного юзера ─────
        var userSimilarities = new List<UserSimilarityStep>();

        foreach (var (otherUserId, otherRatings) in allRatings.Where(x => x.Key != userId))
        {
            var common = myRatings.Keys.Intersect(otherRatings.Keys).ToList();
            if (common.Count < 3) continue;

            var meanA = common.Average(k => myRatings[k]);
            var meanB = common.Average(k => otherRatings[k]);

            var commonMovies = new List<CommonMovieEntry>();
            double numerator = 0, denA = 0, denB = 0;

            foreach (var movieId in common)
            {
                var devA = myRatings[movieId] - meanA;
                var devB = otherRatings[movieId] - meanB;
                var product = devA * devB;

                numerator += product;
                denA += devA * devA;
                denB += devB * devB;

                commonMovies.Add(new CommonMovieEntry(
                    movieId, "",
                    myRatings[movieId], otherRatings[movieId],
                    Math.Round(devA, 3), Math.Round(devB, 3),
                    Math.Round(product, 3)
                ));
            }

            var sqrtDenA = Math.Sqrt(denA);
            var sqrtDenB = Math.Sqrt(denB);
            var correlation = (sqrtDenA == 0 || sqrtDenB == 0)
                ? 0 : numerator / (sqrtDenA * sqrtDenB);

            if (correlation <= 0.01) continue;

            var user = await _users.GetByIdAsync(otherUserId);

            userSimilarities.Add(new UserSimilarityStep(
                otherUserId,
                user?.Username ?? "Unknown",
                common.Count,
                commonMovies.Take(10).ToList(),
                Math.Round(meanA, 3),
                Math.Round(meanB, 3),
                Math.Round(numerator, 4),
                Math.Round(sqrtDenA, 4),
                Math.Round(sqrtDenB, 4),
                Math.Round(correlation, 4)
            ));
        }

        var topSimilar = userSimilarities
            .OrderByDescending(x => x.Correlation)
            .Take(5)
            .ToList();

        // ── передбачення score для нових фільмів ─────
       //var seenIds = myRatings.Keys.ToHashSet();
        var seenIds = await GetSeenIdsAsync(userId);
        var numerators = new Dictionary<Guid, double>();
        var denominators = new Dictionary<Guid, double>();
        var contributors = new Dictionary<Guid, List<ContributingUser>>();

        foreach (var similar in topSimilar)
        {
            var otherRatings = allRatings[similar.OtherUserId];
            foreach (var (movieId, rating) in otherRatings)
            {
                if (seenIds.Contains(movieId) || rating < 7) continue;

                numerators[movieId] = numerators.GetValueOrDefault(movieId)
                    + similar.Correlation * rating;
                denominators[movieId] = denominators.GetValueOrDefault(movieId)
                    + Math.Abs(similar.Correlation);

                if (!contributors.ContainsKey(movieId))
                    contributors[movieId] = new List<ContributingUser>();

                contributors[movieId].Add(new ContributingUser(
                    similar.OtherUserId,
                    similar.OtherUsername,
                    Math.Round(similar.Correlation, 4),
                    rating,
                    Math.Round(similar.Correlation * rating, 4)
                ));
            }
        }

        var predictedScores = numerators
            .Where(kv => denominators[kv.Key] > 0)
            .Select(kv =>
            {
                var predicted = kv.Value / denominators[kv.Key];
                return new PredictedScoreStep(
                    kv.Key, "", null,
                    contributors.GetValueOrDefault(kv.Key, []),
                    Math.Round(kv.Value, 4),
                    Math.Round(denominators[kv.Key], 4),
                    Math.Round(predicted / 10.0, 4)
                );
            })
            .OrderByDescending(x => x.PredictedScore)
            .Take(20)
            .ToList();

        return new CollaborativeLog(
            myRatings.Count,
            userSimilarities.OrderByDescending(x => x.Correlation).Take(10).ToList(),
            topSimilar,
            predictedScores,
            predictedScores.Take(5).ToList()
        );
    }

    // ── Хелпери ───────────────────────────────────────────────

    private async Task<HashSet<Guid>> GetSeenIdsAsync(Guid userId)
    {
        var rated = await _ratings.GetByUserIdAsync(userId);
        var watchList = await _watchList.GetByUserIdAsync(userId);
        return rated.Select(r => r.MovieId)
            .Union(watchList.Where(w => w.Status == "watched").Select(w => w.MovieId))
            .ToHashSet();
    }

    private static (double score, double dot, double magA, double magB,
        List<VectorEntry> commonKeys)
        CosineSimilarityDetailed(
            Dictionary<string, double> a,
            Dictionary<string, double> b)
    {
        var keys = a.Keys.Intersect(b.Keys).ToList();
        var dot = keys.Sum(k => a[k] * b[k]);
        var magA = Math.Sqrt(a.Values.Sum(v => v * v));
        var magB = Math.Sqrt(b.Values.Sum(v => v * v));
        var score = (magA == 0 || magB == 0) ? 0 : dot / (magA * magB);

        var common = keys
            .Where(k => a[k] > 0 && b[k] > 0)
            .Select(k => new VectorEntry(k, Math.Round(a[k] * b[k], 4)))
            .ToList();

        return (score, dot, magA, magB, common);
    }
}