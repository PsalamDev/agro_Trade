using Microsoft.Extensions.Configuration;

namespace AgroTrade.Infrastructure.Email;

/// <summary>Which transport carries the outbound mail.</summary>
public enum EmailTransport
{
    /// <summary>Nothing configured: the message is written to the log instead of being sent.</summary>
    Console,

    /// <summary>MailKit over SMTP.</summary>
    Smtp,

    /// <summary>The Resend HTTP API.</summary>
    Resend,

    /// <summary><c>Email:Provider</c> holds a value that is not recognised.</summary>
    Unknown,
}

/// <summary>
/// Chooses the outbound mail transport from configuration.
/// </summary>
/// <remarks>
/// <para>
/// One place decides, so the runtime registration and the production configuration guard cannot
/// disagree about which transport a deployment would actually use. Under <c>Auto</c> the
/// preference is Resend (the transactional provider, with the delivery receipts the business
/// wants) when its key is present, then SMTP, and finally the console sink so a developer with no
/// mail account still sees the message in the log.
/// </para>
/// <para>
/// The console sink is a development convenience, never a production transport: it is the one
/// selection the production guard rejects outright.
/// </para>
/// </remarks>
public static class EmailTransportSelector
{
    public const string ProviderAuto = "Auto";
    public const string ProviderResend = "Resend";
    public const string ProviderSmtp = "Smtp";
    public const string ProviderConsole = "Console";

    /// <summary>The transport the given configuration selects.</summary>
    public static EmailTransport Resolve(IConfiguration configuration)
    {
        var provider = configuration[$"{EmailOptions.SectionName}:Provider"]?.Trim();
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = ProviderAuto;
        }

        return provider.ToLowerInvariant() switch
        {
            "auto" => Automatic(configuration),
            "resend" => EmailTransport.Resend,
            "smtp" => EmailTransport.Smtp,
            "console" => EmailTransport.Console,
            _ => EmailTransport.Unknown,
        };
    }

    /// <summary>
    /// Whether the provider name is one this application understands. A missing name means
    /// <c>Auto</c>, which is understood.
    /// </summary>
    public static bool IsKnownProvider(string? provider)
    {
        var value = provider?.Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return value.ToLowerInvariant() is "auto" or "resend" or "smtp" or "console";
    }

    private static EmailTransport Automatic(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Resend:ApiKey"]))
        {
            return EmailTransport.Resend;
        }

        if (!string.IsNullOrWhiteSpace(configuration[$"{EmailOptions.SectionName}:Smtp:Host"]))
        {
            return EmailTransport.Smtp;
        }

        return EmailTransport.Console;
    }
}
