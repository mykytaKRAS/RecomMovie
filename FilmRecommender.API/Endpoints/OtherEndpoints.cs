using FilmRecommender.API.Extensions;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Application.Services;
using FilmRecommender.Domain.Interfaces;

namespace FilmRecommender.API.Endpoints;

// ── Genre ─────────────────────────────────────────────────────

public static class GenreEndpoints
{
    public static IEndpointRouteBuilder MapGenreEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/genres", async (IGenreRepository repo) =>
            Results.Ok(await repo.GetAllAsync()))
        .WithTags("Genres")
        .WithName("GetGenres")
        .WithSummary("Список всіх жанрів");

        return app;
    }
}

// ── Rating ────────────────────────────────────────────────────

public static class RatingEndpoints
{
    public static IEndpointRouteBuilder MapRatingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ratings")
            .WithTags("Ratings")
            .RequireAuthorization();

        group.MapGet("/my", async (RatingService svc, HttpContext ctx) =>
            Results.Ok(await svc.GetMyRatingsAsync(ctx.GetUserId())))
        .WithName("GetMyRatings")
        .WithSummary("Мої оцінки фільмів");

        group.MapPost("/", async (CreateRatingRequest req, RatingService svc, HttpContext ctx) =>
        {
            var result = await svc.CreateAsync(ctx.GetUserId(), req);
            return result is null
                ? Results.Conflict(new ErrorResponse("Оцінка для цього фільму вже існує"))
                : Results.Created($"/api/ratings/{result}", new SuccessResponse("Оцінку додано"));
        })
        .WithName("CreateRating")
        .WithSummary("Додати оцінку фільму");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateRatingRequest req, RatingService svc, HttpContext ctx) =>
        {
            var success = await svc.UpdateAsync(ctx.GetUserId(), id, req);
            return success
                ? Results.Ok(new SuccessResponse("Оцінку оновлено"))
                : Results.NotFound(new ErrorResponse("Оцінку не знайдено"));
        })
        .WithName("UpdateRating")
        .WithSummary("Оновити оцінку");

        group.MapDelete("/{id:guid}", async (Guid id, RatingService svc, HttpContext ctx) =>
        {
            var success = await svc.DeleteAsync(ctx.GetUserId(), id);
            return success
                ? Results.Ok(new SuccessResponse("Оцінку видалено"))
                : Results.NotFound(new ErrorResponse("Оцінку не знайдено"));
        })
        .WithName("DeleteRating")
        .WithSummary("Видалити оцінку");

        return app;
    }
}

// ── Survey ────────────────────────────────────────────────────

public static class SurveyEndpoints
{
    public static IEndpointRouteBuilder MapSurveyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/survey")
            .WithTags("Survey")
            .RequireAuthorization();

        group.MapGet("/", async (
            IGenreRepository genreRepo, ISurveyRepository surveyRepo, HttpContext ctx) =>
        {
            var genres = await genreRepo.GetAllAsync();
            var existing = await surveyRepo.GetByUserIdAsync(ctx.GetUserId());

            return Results.Ok(new
            {
                Questions = genres.Select(g => new SurveyQuestionDto(g.Id, g.Name)),
                IsComplete = existing is not null
            });
        })
        .WithName("GetSurvey")
        .WithSummary("Отримати питання опитувальника");

        group.MapPost("/", async (
            SubmitSurveyRequest req, SurveyService svc, HttpContext ctx) =>
        {
            await svc.SubmitAsync(ctx.GetUserId(), req);
            return Results.Ok(new SuccessResponse("Опитувальник збережено"));
        })
        .WithName("SubmitSurvey")
        .WithSummary("Відправити відповіді опитувальника");

        return app;
    }
}

// ── WatchList ─────────────────────────────────────────────────

public static class WatchListEndpoints
{
    public static IEndpointRouteBuilder MapWatchListEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/watchlist")
            .WithTags("WatchList")
            .RequireAuthorization();

        group.MapGet("/", async (WatchListService svc, HttpContext ctx, string? status) =>
        {
            var userId = ctx.GetUserId();
            Console.WriteLine($"WATCHLIST USER ID: {userId}");
            Console.WriteLine($"IS AUTH: {ctx.User.Identity?.IsAuthenticated}");
            Console.WriteLine($"CLAIMS: {string.Join(", ", ctx.User.Claims.Select(c => c.Type + "=" + c.Value))}");
            return Results.Ok(await svc.GetByUserAsync(userId, status));
        });

        group.MapPost("/", async (
            AddToWatchListRequest req, WatchListService svc, HttpContext ctx) =>
        {
            var result = await svc.AddAsync(ctx.GetUserId(), req);
            return result is null
                ? Results.Conflict(new ErrorResponse("Фільм вже у списку"))
                : Results.Created($"/api/watchlist/{result}", new SuccessResponse("Додано до списку"));
        })
        .WithName("AddToWatchList")
        .WithSummary("Додати фільм до списку");

        group.MapPut("/{id:guid}", async (
            Guid id, UpdateWatchStatusRequest req, WatchListService svc, HttpContext ctx) =>
        {
            var valid = new[] { "want", "watching", "watched" };
            if (!valid.Contains(req.Status))
                return Results.BadRequest(new ErrorResponse("Статус має бути: want, watching або watched"));

            var success = await svc.UpdateStatusAsync(ctx.GetUserId(), id, req.Status);
            return success
                ? Results.Ok(new SuccessResponse("Статус оновлено"))
                : Results.NotFound(new ErrorResponse("Запис не знайдено"));
        })
        .WithName("UpdateWatchStatus")
        .WithSummary("Оновити статус перегляду");

        group.MapDelete("/{id:guid}", async (Guid id, WatchListService svc, HttpContext ctx) =>
        {
            var success = await svc.DeleteAsync(ctx.GetUserId(), id);
            return success
                ? Results.Ok(new SuccessResponse("Видалено зі списку"))
                : Results.NotFound(new ErrorResponse("Запис не знайдено"));
        })
        .WithName("DeleteFromWatchList")
        .WithSummary("Видалити зі списку");

        return app;
    }
}