namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Outbound email settings (plan D3). Leaving <see cref="SmtpSettings.Host"/> blank keeps the
/// console sink, so a developer with no mail server still sees confirmation and reset messages
/// in the log; setting it switches delivery to MailKit/SMTP.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>The envelope sender. Required once SMTP is configured.</summary>
    public string? FromAddress { get; set; }

    /// <summary>Display name shown beside <see cref="FromAddress"/>.</summary>
    public string FromName { get; set; } = "Achievers Global Consult";

    public SmtpSettings Smtp { get; set; } = new();

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
}
