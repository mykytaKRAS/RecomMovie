using System.Security.Claims;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Application.Services;
using FilmRecommender.Domain.Interfaces;
using FilmRecommender.Domain.Models;

namespace FilmRecommender.API.Extensions;

public static class MovieEndpoints
{
    public static IEndpointRouteBuilder MapMovieEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/movies").WithTags("Movies");

        group.MapGet("/", async (
            IMovieRepository repo,
            int? genreId, short? yearFrom, short? yearTo,
            decimal? minRating, string? language, string? sortBy,
            int page = 1, int pageSize = 20) =>
        {
            var filter = new MovieFilter
            {
                GenreId = genreId,
                YearFrom = yearFrom,
                YearTo = yearTo,
                MinRating = minRating,
                Language = language,
                SortBy = sortBy
            };

            pageSize = Math.Clamp(pageSize, 1, 50);

            var items = await repo.GetAllAsync(filter, page, pageSize);
            var total = await repo.CountAsync(filter);

            var dtos = items.Select(m => new MovieSummaryDto(
                m.Id, m.Title, m.ReleaseYear, m.AvgRating, m.PosterPath,
                m.Genres.Select(g => g.Name)));

            return Results.Ok(new PagedResult<MovieSummaryDto>(dtos, total, page, pageSize));
        })
        .WithName("GetMovies")
        .WithSummary("Список фільмів з фільтрами та пагінацією");

        group.MapGet("/search", async (
            string q, IMovieRepository repo, int page = 1, int pageSize = 20) =>
        {
            if (string.IsNullOrWhiteSpace(q))
                return Results.BadRequest(new ErrorResponse("Параметр пошуку не може бути порожнім"));

            var items = await repo.SearchAsync(q, page, pageSize);
            var dtos = items.Select(m => new MovieSummaryDto(
                m.Id, m.Title, m.ReleaseYear, m.AvgRating, m.PosterPath, []));

            return Results.Ok(dtos);
        })
        .WithName("SearchMovies")
        .WithSummary("Пошук фільмів за назвою");

        group.MapGet("/{id:guid}", async (
            Guid id, IMovieRepository repo,
            IUserRatingRepository ratingRepo, HttpContext ctx) =>
        {
            var movie = await repo.GetByIdAsync(id);
            if (movie is null)
                return Results.NotFound(new ErrorResponse("Фільм не знайдено"));

            UserRatingDto? userRating = null;
            var userIdClaim = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(userIdClaim, out var userId))
            {
                var rating = await ratingRepo.GetByUserAndMovieAsync(userId, id);
                if (rating is not null)
                    userRating = new UserRatingDto(
                        rating.Id, rating.Rating, rating.Review, rating.RatedAt);
            }

            var dto = new MovieDetailDto(
                movie.Id,
                movie.Title,
                movie.OriginalTitle,
                movie.Description,
                movie.ReleaseYear,
                movie.DurationMin,
                movie.AvgRating,
                movie.VoteCount,
                movie.PosterPath,
                movie.OriginalLanguage,
                movie.Genres.Select(g => g.Name),
                movie.Actors.Select(a => new ActorDto(a.FullName, a.RoleName)),
                movie.Directors.Select(d => d.FullName),
                movie.Countries.Select(c => c.Name),
                movie.Tags.Select(t => t.Name),
                userRating);

            return Results.Ok(dto);
        })
        .WithName("GetMovieById")
        .WithSummary("Деталі фільму");

        return app;
    }
}