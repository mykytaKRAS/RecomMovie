using System.Security.Claims;

namespace FilmRecommender.API.Extensions;

public static class HttpContextExtensions
{
    public static Guid GetUserId(this HttpContext ctx)
    {
        var claim = ctx.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }
}