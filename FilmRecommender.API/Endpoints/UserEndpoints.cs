using FilmRecommender.API.Extensions;
using FilmRecommender.Application.DTOs;
using FilmRecommender.Domain.Interfaces;

namespace FilmRecommender.API.Endpoints;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/me", async (IUserRepository repo, HttpContext ctx) =>
        {
            var user = await repo.GetByIdAsync(ctx.GetUserId());
            return user is null
                ? Results.NotFound(new ErrorResponse("Користувача не знайдено"))
                : Results.Ok(new UserProfileDto(user.Id, user.Username, user.Email, user.CreatedAt));
        })
        .WithName("GetMe")
        .WithSummary("Профіль поточного користувача");

        //test
        /*group.MapGet("/test-auth", (HttpContext ctx) =>
        {
            return Results.Ok(new
            {
                isAuth = ctx.User.Identity?.IsAuthenticated,
                claims = ctx.User.Claims.Select(c => new { c.Type, c.Value })
            });
        }).RequireAuthorization();


        group.MapGet("/debug-token", (HttpContext ctx) =>
        {
            var auth = ctx.Request.Headers.Authorization.ToString();
            Console.WriteLine("AUTH HEADER: " + auth);

            return Results.Ok(auth);
        });*/

        return app;
    }
}