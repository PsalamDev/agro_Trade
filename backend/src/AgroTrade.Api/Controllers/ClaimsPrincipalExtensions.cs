using System.Security.Claims;

namespace AgroTrade.Api.Controllers;

public static class ClaimsPrincipalExtensions
{
    /// <summary>User id from the JWT 'sub' claim (falls back to ASP.NET's nameidentifier).</summary>
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue("sub");
        return Guid.TryParse(subject, out var id)
            ? id
            : throw new InvalidOperationException("Authenticated principal has no user id claim.");
    }
}