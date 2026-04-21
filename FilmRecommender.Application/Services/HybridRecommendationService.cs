using FilmRecommender.Application.DTOs;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.Application.Services;

public class HybridRecommendationService
{
    private readonly ContentBasedService _contentBased;
    private readonly CollaborativeService _collaborative;
    private readonly IUserRatingExtendedRepository _ratingsExt;
    private readonly IRecommendationRepository _recommendations;

    public const int CollaborativeThreshold = 100;

    public HybridRecommendationService(
        ContentBasedService contentBased,
        CollaborativeService collaborative,
        IUserRatingExtendedRepository ratingsExt,
        IRecommendationRepository recommendations)
    {
        _contentBased = contentBased;
        _collaborative = collaborative;
        _ratingsExt = ratingsExt;
        _recommendations = recommendations;
    }

    public async Task<(IEnumerable<Recommendation> Results, string UsedAlgorithm)>
        GetRecommendationsAsync(Guid userId, string? algorithm = null, int limit = 20)
    {
        var ratingCount = await _ratingsExt.CountByUserIdAsync(userId);
        var canCollaborative = ratingCount >= CollaborativeThreshold;

        // Визначаємо алгоритм
        var selected = algorithm switch
        {
            "collaborative" when canCollaborative => "collaborative",
            "collaborative" when !canCollaborative => "content_based", // fallback
            "content_based" => "content_based",
            _ => "content_based"
        };

        IEnumerable<Recommendation> results;

        if (selected == "collaborative")
        {
            results = await _collaborative.GetRecommendationsAsync(userId, limit);

            // Якщо collaborative дало мало результатів — доповнюємо content-based
            var resultList = results.ToList();
            /*if (resultList.Count < limit / 2)
            {
                var contentResults = await _contentBased
                    .GetRecommendationsAsync(userId, limit - resultList.Count);

                var existingMovieIds = resultList.Select(r => r.MovieId).ToHashSet();
                var additional = contentResults
                    .Where(r => !existingMovieIds.Contains(r.MovieId))
                    .ToList();

                resultList.AddRange(additional);
                results = resultList;
                selected = "hybrid";
            }
            else
            {
                results = resultList;
            }*/

            results = resultList;
        }
        else
        {
            results = await _contentBased.GetRecommendationsAsync(userId, limit);
        }

        var resultsFinal = results.ToList();

        // Зберігаємо в БД (очищаємо старі і записуємо нові)
        await _recommendations.DeleteByUserIdAsync(userId);
        if (resultsFinal.Any())
            await _recommendations.SaveManyAsync(resultsFinal);

        return (resultsFinal, selected);
    }

    public async Task<RecommendationStatusDto> GetStatusAsync(Guid userId)
    {
        var count = await _ratingsExt.CountByUserIdAsync(userId);
        return new RecommendationStatusDto(
            RatingCount: count,
            CollaborativeAvailable: count >= CollaborativeThreshold,
            RatingsUntilCollaborative: Math.Max(0, CollaborativeThreshold - count)
        );
    }
}