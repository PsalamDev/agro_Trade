using System.Collections.Concurrent;
using AgroTrade.Application.Common;

namespace AgroTrade.Tests.Integration;

/// <summary>
/// Captures outbound email instead of sending it, standing in for the console sink so a test can
/// read the reset code the API emails. Registered by <see cref="TestingWebAppFactory"/> in place of
/// the real <see cref="IEmailSender"/>.
/// </summary>
public sealed class RecordingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _messages = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        _messages.Enqueue(message);
        return Task.CompletedTask;
    }

    /// <summary>The most recent message addressed to <paramref name="to"/>, or null if none.</summary>
    public EmailMessage? LastTo(string to) =>
        _messages.LastOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase));
}
