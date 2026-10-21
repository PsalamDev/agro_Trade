namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Outbound email settings (plan D3). The transport is chosen by <see cref="Provider"/> and the
/// settings of that transport; leaving everything blank keeps the console sink, so a developer with
/// no mail account still sees confirmation and reset messages in the log.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>
    /// Which transport carries the mail: <c>Auto</c> (default), <c>Resend</c>, <c>Smtp</c> or
    /// <c>Console</c>. Auto prefers Resend when its key is present, then SMTP, and falls back to
    /// the console sink. See <see cref="EmailTransportSelector"/> for the exact rules.
    /// </summary>
    public string Provider { get; set; } = "Auto";

    /// <summary>The envelope sender. Required once a real transport is configured.</summary>
    public string? FromAddress { get; set; }

    /// <summary>Display name shown beside <see cref="FromAddress"/>.</summary>
    public string FromName { get; set; } = "Achievers Global Consult";

    public SmtpSettings Smtp { get; set; } = new();

    public ResendSettings Resend { get; set; } = new();

    public sealed class SmtpSettings
    {
        /// <summary>Blank selects the console sink instead of dialling a server.</summary>
        public string? Host { get; set; }

        public int Port { get; set; } = 587;

        /// <summary>
        /// <c>Auto</c> (default), <c>StartTls</c>, <c>SslOnConnect</c> or <c>None</c>. Auto
        /// negotiates: implicit TLS on 465, STARTTLS on 587/25 when the server offers it.
        /// </summary>
        public string SecureSocket { get; set; } = "Auto";

        public string? Username { get; set; }

        public string? Password { get; set; }

        public int TimeoutSeconds { get; set; } = 30;
    }

    public sealed class ResendSettings
    {
        /// <summary>
        /// The Resend API key (<c>re_...</c>). Its presence is what selects Resend under
        /// <see cref="Provider"/> = <c>Auto</c>.
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// The API base address. Overridable so a regional endpoint or a local proxy can be used
        /// without a code change.
        /// </summary>
        public string BaseUrl { get; set; } = "https://api.resend.com";

        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>How many times a transient failure is retried before the send is failed.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>
        /// The first backoff step; each retry doubles it. Kept short by default because the
        /// caller's own request timeout matters more than the provider's patience.
        /// </summary>
        public int RetryBaseDelayMilliseconds { get; set; } = 500;
    }
}
