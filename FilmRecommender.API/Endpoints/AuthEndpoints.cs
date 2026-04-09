using FilmRecommender.Application.DTOs;
using FilmRecommender.Application.Services;

namespace FilmRecommender.API.Extensions;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest req, AuthService svc) =>
        {
            var result = await svc.RegisterAsync(req);
            return result is null
                ? Results.Conflict(new ErrorResponse("Email або username вже зайнятий"))
                : Results.Ok(result);
        })
        .WithName("Register")
        .WithSummary("Реєстрація нового користувача");

        group.MapPost("/login", async (LoginRequest req, AuthService svc) =>
        {
            var result = await svc.LoginAsync(req);
            return result is null
                ? Results.Unauthorized()
                : Results.Ok(result);
        })
        .WithName("Login")
        .WithSummary("Вхід в систему");

        return app;
    }
}