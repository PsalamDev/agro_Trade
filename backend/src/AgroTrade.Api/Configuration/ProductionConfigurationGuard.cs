using Microsoft.Extensions.Configuration;

namespace AgroTrade.Api.Configuration;

/// <summary>
/// Fails fast when the app is started outside Development with configuration that only makes
/// sense on a developer machine. These are the settings that otherwise degrade silently:
/// email falls back to the console sink, links point at localhost, or the API trusts a dev
/// frontend origin.
/// </summary>
public static class ProductionConfigurationGuard
{
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]))
        {
            errors.Add(
                "Email:Smtp:Host must be set outside Development. Without it the API silently uses " +
                "the console email sender and users cannot confirm their accounts.");
        }

        if (string.IsNullOrWhiteSpace(configuration["Email:FromAddress"]))
        {
            errors.Add("Email:FromAddress must be set outside Development.");
        }

        if (configuration.GetValue("Auth:AutoConfirmEmail", false))
        {
            errors.Add("Auth:AutoConfirmEmail must be false outside Development.");
        }

        var appBaseUrl = configuration["Auth:AppBaseUrl"];
        if (!TryCreatePublicUrl(appBaseUrl, out _))
        {
            errors.Add(
                "Auth:AppBaseUrl must be an absolute HTTPS URL on a non-local host outside " +
                "Development; it is used in confirmation and password-reset emails.");
        }

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (allowedOrigins.Length == 0)
        {
            errors.Add("Cors:AllowedOrigins must name at least one frontend origin outside Development.");
        }
        else
        {
            foreach (var origin in allowedOrigins)
            {
                if (!TryCreatePublicUrl(origin, out _))
                {
                    errors.Add(
                        $"Cors:AllowedOrigins contains '{origin}', which is not an absolute HTTPS " +
                        "URL on a non-local host.");
                }
            }
        }

        return errors;
    }

    private static bool TryCreatePublicUrl(string? value, out Uri? uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (string.Equals(parsed.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(parsed.Host, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(parsed.Host, "::1", StringComparison.Ordinal))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}
