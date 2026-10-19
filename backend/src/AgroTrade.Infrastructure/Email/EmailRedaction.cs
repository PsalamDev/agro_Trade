namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// An address is personal data, so it is masked before it reaches the log
/// (architecture §6 — "no PII in logs"). Enough to correlate a delivery, not enough to leak it.
/// </summary>
internal static class EmailRedaction
{
    public static string Mask(string address)
    {
        var at = address.IndexOf('@');
        if (at <= 0)
        {
            return "***";
        }

        var local = address[..at];
        var visible = local.Length <= 1 ? "*" : $"{local[0]}***";
        return $"{visible}{address[at..]}";
    }
}
