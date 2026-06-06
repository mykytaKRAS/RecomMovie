// FilmRecommender.Application/Services/EvaluationService.cs

using FilmRecommender.Domain.Interfaces;

namespace FilmRecommender.Application.Services;

public record ContentBasedMetrics(
    double PrecisionAt10,
    double RecallAt10,
    int TestedUsers,
    int TotalRelevantFound,
    int TotalRecommended
);

public record CollaborativeMetrics(
    double Rmse,
    double Mae,
    int TestedUsers,
    int PredictedPairs
);

public class EvaluationService
{
    private readonly IUserRatingExtendedRepository _ratingsExt;
    private readonly ContentBasedService _contentBased;
    private readonly CollaborativeService _collaborative;

    private const double RelevanceThreshold = 7.0;
    private const double TestSplitRatio = 0.2;
    private const int MinRatingsForTest = 20;
    private const int K = 10;

    public EvaluationService(
        IUserRatingExtendedRepository ratingsExt,
        ContentBasedService contentBased,
        CollaborativeService collaborative)
    {
        _ratingsExt = ratingsExt;
        _contentBased = contentBased;
        _collaborative = collaborative;
    }

    public async Task<ContentBasedMetrics> EvaluateContentBasedAsync()
    {
        var allRatings = await _ratingsExt.GetAllGroupedByUserAsync();

        var eligibleUsers = allRatings
            .Where(kv => kv.Value.Count >= MinRatingsForTest)
            .ToList();

        if (!eligibleUsers.Any())
            return new ContentBasedMetrics(0, 0, 0, 0, 0);

        double totalPrecision = 0;
        double totalRecall = 0;
        int testedUsers = 0;
        int totalRelevantFound = 0;
        int totalRecommended = 0;

        foreach (var (userId, userRatings) in eligibleUsers)
        {
            var relevantItems = userRatings
                .Where(r => r.Value >= RelevanceThreshold)
                .ToList();

            if (relevantItems.Count < 3) continue;

            int testCount = Math.Max(1, (int)(relevantItems.Count * TestSplitRatio));
            var testSet = relevantItems
                .OrderBy(_ => Guid.NewGuid())
                .Take(testCount)
                .ToDictionary(r => r.Key, r => r.Value);
            var testMovieIds = testSet.Keys.ToHashSet();

            var trainRatings = userRatings
                .Where(r => !testMovieIds.Contains(r.Key))
                .ToDictionary(r => r.Key, r => r.Value);

            // Exclude тільки train — test лишаємо як кандидати
            var excludeIds = trainRatings.Keys.ToHashSet();

            var recs = await _contentBased.GetRecommendationsForEvalAsync(
                userId, trainRatings, excludeIds, K * 3);

            var topK = recs
                .OrderByDescending(r => r.Score)
                .Take(K)
                .Select(r => r.MovieId)
                .ToHashSet();

            if (!topK.Any()) continue;

            int hits = topK.Count(id => testMovieIds.Contains(id));

            totalPrecision += (double)hits / K;
            totalRecall += testMovieIds.Count > 0 ? (double)hits / testMovieIds.Count : 0;
            totalRelevantFound += hits;
            totalRecommended += topK.Count;
            testedUsers++;
        }

        if (testedUsers == 0)
            return new ContentBasedMetrics(0, 0, 0, 0, 0);

        return new ContentBasedMetrics(
            PrecisionAt10: Math.Round(totalPrecision / testedUsers, 4),
            RecallAt10: Math.Round(totalRecall / testedUsers, 4),
            TestedUsers: testedUsers,
            TotalRelevantFound: totalRelevantFound,
            TotalRecommended: totalRecommended
        );
    }

    public async Task<CollaborativeMetrics> EvaluateCollaborativeAsync()
    {
        var allRatings = await _ratingsExt.GetAllGroupedByUserAsync();

        var eligibleUsers = allRatings
            .Where(kv => kv.Value.Count >= MinRatingsForTest)
            .ToList();

        if (!eligibleUsers.Any())
            return new CollaborativeMetrics(0, 0, 0, 0);

        double sumSquaredError = 0;
        double sumAbsError = 0;
        int totalPairs = 0;
        int testedUsers = 0;

        foreach (var (userId, userRatings) in eligibleUsers)
        {
            int testCount = Math.Max(1, (int)(userRatings.Count * TestSplitRatio));
            var testSet = userRatings
                .OrderBy(_ => Guid.NewGuid())
                .Take(testCount)
                .ToDictionary(r => r.Key, r => r.Value);
            var testMovieIds = testSet.Keys.ToHashSet();

            var trainRatings = userRatings
                .Where(r => !testMovieIds.Contains(r.Key))
                .ToDictionary(r => r.Key, r => r.Value);

            if (trainRatings.Count < 10) continue;

            // У allRatings замінюємо поточного юзера на train
            var allRatingsForEval = allRatings
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Key == userId ? trainRatings : kv.Value);

            // Exclude тільки train — test лишаємо як кандидати
            var excludeOnlyTrain = trainRatings.Keys.ToHashSet();

            var recs = _collaborative.GetRecommendationsForEval(
                userId, trainRatings, allRatingsForEval, excludeOnlyTrain, 5000);

            // Денормалізуємо score назад у 1-10
            var recDict = recs.ToDictionary(
                r => r.MovieId,
                r => (double)r.Score * 10.0);

            foreach (var (movieId, actualRating) in testSet)
            {
                if (!recDict.TryGetValue(movieId, out double predicted)) continue;

                double error = predicted - actualRating;
                sumSquaredError += error * error;
                sumAbsError += Math.Abs(error);
                totalPairs++;
            }

            testedUsers++;
        }

        if (totalPairs == 0)
            return new CollaborativeMetrics(0, 0, testedUsers, 0);

        return new CollaborativeMetrics(
            Rmse: Math.Round(Math.Sqrt(sumSquaredError / totalPairs), 4),
            Mae: Math.Round(sumAbsError / totalPairs, 4),
            TestedUsers: testedUsers,
            PredictedPairs: totalPairs
        );
    }
}