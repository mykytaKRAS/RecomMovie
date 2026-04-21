using FilmRecommender.API.Extensions;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Application.Services;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Infrastructure.Services;

namespace FilmRecommender.API.Endpoints;

public static class RecommendationEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/recommendations")
            .WithTags("Recommendations")
            .RequireAuthorization();

        // GET /api/recommendations/status
        // Показує скільки оцінок є і чи доступна collaborative
        group.MapGet("/status", async (
            HybridRecommendationService svc, HttpContext ctx) =>
        {
            var status = await svc.GetStatusAsync(ctx.GetUserId());
            return Results.Ok(status);
        })
        .WithName("GetRecommendationStatus")
        .WithSummary("Статус доступності алгоритмів рекомендацій")
        .AllowAnonymous();

        // GET /api/recommendations?algorithm=content_based
        // Генерує і повертає рекомендації
        group.MapGet("/", async (
            HybridRecommendationService svc,
            IMovieRepository movies,
            HttpContext ctx,
            string? algorithm) =>
        {
            var userId = ctx.GetUserId();
            var (results, usedAlgorithm) = await svc
                .GetRecommendationsAsync(userId, algorithm);

            var dtos = new List<RecommendationResultDto>();
            foreach (var rec in results)
            {
                var movie = await movies.GetByIdAsync(rec.MovieId);
                if (movie is null) continue;

                dtos.Add(new RecommendationResultDto(
                    rec.Id,
                    new MovieSummaryDto(
                        movie.Id,
                        movie.Title,
                        movie.ReleaseYear,
                        movie.AvgRating,
                        movie.PosterPath,
                        movie.Genres.Select(g => g.Name)),
                    rec.Score,
                    usedAlgorithm));
            }

            return Results.Ok(new
            {
                Algorithm = usedAlgorithm,
                TotalCount = dtos.Count,
                Recommendations = dtos
            });
        })
        .WithName("GetRecommendations")
        .WithSummary("Отримати персональні рекомендації фільмів");

        // POST /api/recommendations/vectorize
        // Запускає векторизацію всіх фільмів (адмін endpoint)
        group.MapPost("/vectorize", async (
            FeatureVectorizationService vectorizer) =>
        {
            await vectorizer.VectorizeAllMoviesAsync();
            return Results.Ok(new SuccessResponse("Векторизацію завершено"));
        })
        .WithName("VectorizeMovies")
        .WithSummary("Запустити векторизацію фільмів (одноразово після імпорту)");

        // GET /api/recommendations/explain/content
        group.MapGet("/explain/content", async (
            RecommendationExplainerService explainer, HttpContext ctx) =>
        {
            var log = await explainer.ExplainContentBasedAsync(ctx.GetUserId());
            return Results.Ok(log);
        })
        .WithName("ExplainContentBased")
        .WithSummary("Детальний лог розрахунків content-based");

        // GET /api/recommendations/explain/collaborative
        group.MapGet("/explain/collaborative", async (
            RecommendationExplainerService explainer, HttpContext ctx) =>
        {
            var log = await explainer.ExplainCollaborativeAsync(ctx.GetUserId());
            return Results.Ok(log);
        })
        .WithName("ExplainCollaborative")
        .WithSummary("Детальний лог розрахунків collaborative");

        return app;
    }
}