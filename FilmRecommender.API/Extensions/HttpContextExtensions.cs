using System.Security.Claims;

namespace FilmRecommender.API.Extensions;

public static class HttpContextExtensions
{
    public static Guid GetUserId(this HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                 ?? ctx.User.FindFirst("sub")?.Value
                 ?? ctx.User.FindFirst("nameid")?.Value;

        Console.WriteLine($"GET USER ID CLAIM: '{claim}'");
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}