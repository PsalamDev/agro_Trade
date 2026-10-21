using AgroTrade.Infrastructure.Email;
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

        var provider = configuration[$"{EmailOptions.SectionName}:Provider"];
        if (!EmailTransportSelector.IsKnownProvider(provider))
        {
            errors.Add(
                $"Email:Provider '{provider}' is not one of Auto, Resend, Smtp or Console.");
        }
        else
        {
            switch (EmailTransportSelector.Resolve(configuration))
            {
                case EmailTransport.Resend:
                    if (string.IsNullOrWhiteSpace(configuration["Email:Resend:ApiKey"]))
                    {
                        errors.Add(
                            "Email:Resend:ApiKey must be set outside Development when Resend is the " +
                            "mail transport.");
                    }
                    break;
                case EmailTransport.Smtp:
                    if (string.IsNullOrWhiteSpace(configuration["Email:Smtp:Host"]))
                    {
                        errors.Add(
                            "Email:Smtp:Host must be set outside Development when SMTP is the mail " +
                            "transport.");
                    }
                    break;
                default:
                    errors.Add(
                        "No mail transport is configured outside Development. Set Email:Resend:ApiKey " +
                        "(preferred) or Email:Smtp:Host; without one every message is only written to " +
                        "the log and users never receive their confirmation.");
                    break;
            }
        }

        var fromAddress = configuration["Email:FromAddress"];
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            errors.Add("Email:FromAddress must be set outside Development.");
        }
        else if (!fromAddress.Contains('@'))
        {
            errors.Add("Email:FromAddress must be an email address.");
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
