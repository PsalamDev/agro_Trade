namespace AgroTrade.Application.Trade;

/// <summary>
/// Gateway configuration, bound from the <c>Payments</c> section (roadmap §5.5).
/// </summary>
public sealed class PaymentOptions
{
    public const string SectionName = "Payments";

    /// <summary>
    /// Shared secret used to sign and verify webhook bodies with HMAC-SHA256.
    /// </summary>
    /// <remarks>
    /// This is the only thing standing between a stranger with a URL and the ability to mark
    /// invoices paid, so it is validated on start rather than allowed to be blank. A real
    /// adapter would instead use the provider's published signing scheme and per-endpoint
    /// secrets; the sandbox's scheme is deliberately the simplest thing that is actually a
    /// signature rather than a shared value compared for equality.
    /// </remarks>
    public string SigningSecret { get; set; } = string.Empty;

    /// <summary>
    /// Currencies the sandbox gateway settles. The roadmap scopes task 2.4 to one corridor;
    /// this is where that limit lives, and a payment in another currency is refused rather
    /// than raised into a charge that will never arrive.
    /// </summary>
    public string[] SupportedCurrencies { get; set; } = ["USD"];

    /// <summary>
    /// Base address used to build the payer's return URL. Empty in dev, where the SPA and API
    /// share an origin under the Vite proxy.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
}
