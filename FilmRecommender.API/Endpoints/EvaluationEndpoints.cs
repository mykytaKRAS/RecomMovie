using FilmRecommender.API.Extensions;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Application.Services;
using FilmRecommender.Domain.Interfaces;

namespace FilmRecommender.API.Endpoints;

public static class EvaluationEndpoints
{
    public static IEndpointRouteBuilder MapEvaluationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/evaluation")
            .WithTags("Evaluation");

        // GET /api/evaluation
        // Повертає RMSE для collaborative та Precision@10 для content-based
        group.MapGet("/", async (EvaluationService svc) =>
        {
            Console.WriteLine("Evaluation started...");

            var contentTask = svc.EvaluateContentBasedAsync();
            var collabTask = svc.EvaluateCollaborativeAsync();

            await Task.WhenAll(contentTask, collabTask);

            return Results.Ok(new
            {
                contentBased = await contentTask,
                collaborative = await collabTask,
                generatedAt = DateTime.UtcNow
            });
        })
        .WithName("Evaluate")
        .WithSummary("Оцінка якості алгоритмів: Precision@10 та RMSE");

        // POST /api/recommendations/{id}/click
        // Фіксує що користувач перейшов на фільм з рекомендацій
        app.MapPost("/api/recommendations/{id:guid}/click", async (
            Guid id,
            IRecommendationRepository repo,
            HttpContext ctx) =>
        {
            await repo.MarkClickedAsync(id);
            return Results.Ok(new SuccessResponse("Click recorded"));
        })
        .WithTags("Recommendations")
        .WithName("MarkRecommendationClicked")
        .WithSummary("Зафіксувати перехід користувача на рекомендований фільм")
        .RequireAuthorization();

        return app;
    }
}