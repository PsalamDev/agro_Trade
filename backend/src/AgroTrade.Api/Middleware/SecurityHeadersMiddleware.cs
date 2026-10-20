namespace AgroTrade.Api.Middleware;

/// <summary>
/// Applies a conservative baseline of browser security headers to every response.
///
/// <para>
/// The API answers JSON, so its CSP can be locked to nothing. Uploaded files are different:
/// they are user-supplied documents and images, and must never be treated as a page that can
/// run script. Non-image uploads are forced to download; images stay inline for the marketplace
/// to render them.
/// </para>
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    private static readonly HashSet<string> InlineImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg",
        ".jpeg",
        ".png",
        ".webp",
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["X-Permitted-Cross-Domain-Policies"] = "none";

        if (context.Request.Path.StartsWithSegments("/api"))
        {
            // A JSON response has no business loading anything. Swagger lives outside /api.
            headers["Content-Security-Policy"] =
                "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
        }
        else if (context.Request.Path.StartsWithSegments("/uploads"))
        {
            // Even an uploaded document that somehow renders as a page cannot run script or
            // inherit this origin.
            headers["Content-Security-Policy"] = "sandbox; frame-ancestors 'none'";

            var extension = Path.GetExtension(context.Request.Path.Value ?? string.Empty);
            if (!InlineImageExtensions.Contains(extension))
            {
                headers["Content-Disposition"] = "attachment";
            }
        }

        await next(context);
    }
}
