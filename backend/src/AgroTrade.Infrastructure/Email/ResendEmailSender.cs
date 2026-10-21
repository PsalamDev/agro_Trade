using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgroTrade.Application.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgroTrade.Infrastructure.Email;

/// <summary>
/// Delivery through the Resend HTTP API (plan D3). Registered when <c>Email:Resend:ApiKey</c> is
/// set, or when <c>Email:Provider</c> names Resend explicitly.
/// </summary>
/// <remarks>
/// <para>
/// The contract is the same as the SMTP transport: one branded message in, one delivery out. The
/// inline brand mark travels as a base64 attachment carrying a <c>content_id</c>, which is how
/// Resend resolves the <c>cid:</c> references in the branded HTML.
/// </para>
/// <para>
/// A transient failure (timeout, dropped connection, 429, 5xx) is retried with a backoff and the
/// same idempotency key, so a retry cannot deliver the message twice. A rejection (4xx) is not
/// retried: the provider has already decided, and repeating it would only burn quota. The API
/// key never appears in a log line or an exception message.
/// </para>
/// </remarks>
public sealed class ResendEmailSender(
    HttpClient client,
    IOptions<EmailOptions> options,
    ILogger<ResendEmailSender> logger) : IEmailSender
{
    private const string EmailsPath = "emails";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly EmailOptions _options = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        var apiKey = _options.Resend.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Email:Resend:ApiKey must be set before a message can be sent through Resend.");
        }

        if (string.IsNullOrWhiteSpace(_options.FromAddress))
        {
            throw new InvalidOperationException(
                "Email:FromAddress must be set before a message can be sent through Resend.");
        }

        var payload = BuildRequest(message);

        // One key for the whole call: a retry after a timeout must not deliver the message twice.
        // A fresh key per call keeps two genuinely separate emails (two password resets, say)
        // from collapsing into one.
        var idempotencyKey = Guid.NewGuid().ToString("N");
        var attempts = Math.Max(1, _options.Resend.MaxRetries + 1);

        for (var attempt = 1; ; attempt++)
        {
            Exception failure;

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, EmailsPath)
                {
                    Content = JsonContent.Create(payload, options: SerializerOptions),
                };
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

                using var response = await client.SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    var messageId = await ReadMessageIdAsync(response, ct);
                    logger.LogInformation(
                        "Sent '{Subject}' to {Recipient} via Resend ({MessageId}).",
                        message.Subject,
                        EmailRedaction.Mask(message.To),
                        messageId ?? "no id");
                    return;
                }

                var detail = await ReadDetailAsync(response, ct);
                if (!IsTransient(response.StatusCode))
                {
                    throw new ResendDeliveryException(
                        $"Resend rejected the message with status {(int)response.StatusCode}: {detail}");
                }

                failure = new ResendDeliveryException(
                    $"Resend returned status {(int)response.StatusCode}: {detail}");
            }
            catch (HttpRequestException ex)
            {
                failure = ex;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                // The client timeout fired rather than the caller cancelling.
                failure = ex;
            }

            if (attempt >= attempts)
            {
                throw new ResendDeliveryException(
                    $"Resend could not deliver the message after {attempt} attempt(s).",
                    failure);
            }

            logger.LogWarning(
                failure,
                "Resend attempt {Attempt} of {Attempts} failed; retrying.",
                attempt,
                attempts);

            await Task.Delay(Backoff(attempt), ct);
        }
    }

    private ResendEmailRequest BuildRequest(EmailMessage message)
    {
        var from = string.IsNullOrWhiteSpace(_options.FromName)
            ? _options.FromAddress!
            : $"{_options.FromName} <{_options.FromAddress}>";

        IReadOnlyList<ResendAttachment>? attachments = null;
        if (message.InlineImages is { Count: > 0 } images)
        {
            attachments = images
                .Select(image => new ResendAttachment(
                    image.FileName,
                    Convert.ToBase64String(image.Content),
                    image.ContentType,
                    image.ContentId))
                .ToList();
        }

        return new ResendEmailRequest(
            from,
            [message.To],
            message.Subject,
            message.HtmlBody,
            message.PlainTextBody,
            attachments);
    }

    /// <summary>
    /// The failures worth trying again: the provider is busy, overloaded, or briefly unreachable.
    /// A rejection is not transient and is never retried.
    /// </summary>
    private static bool IsTransient(HttpStatusCode status) => status switch
    {
        HttpStatusCode.RequestTimeout => true,
        HttpStatusCode.TooManyRequests => true,
        HttpStatusCode.InternalServerError => true,
        HttpStatusCode.BadGateway => true,
        HttpStatusCode.ServiceUnavailable => true,
        HttpStatusCode.GatewayTimeout => true,
        _ => false,
    };

    private TimeSpan Backoff(int attempt)
    {
        var baseDelay = Math.Max(0, _options.Resend.RetryBaseDelayMilliseconds);
        var delay = baseDelay * Math.Pow(2, attempt - 1);
        return TimeSpan.FromMilliseconds(Math.Min(delay, 30_000));
    }

    private static async Task<string?> ReadMessageIdAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);

            return document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            // A body without an id is not a failure: the send succeeded, the receipt is just thin.
            return null;
        }
    }

    private static async Task<string> ReadDetailAsync(
        HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            return "no detail";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? "no detail";
            }

            if (document.RootElement.TryGetProperty("name", out var name))
            {
                return name.GetString() ?? "no detail";
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall through to the raw body.
        }

        return body.Length > 300 ? body[..300] : body;
    }

    private sealed record ResendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] string[] To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string? Text,
        [property: JsonPropertyName("attachments")] IReadOnlyList<ResendAttachment>? Attachments);

    private sealed record ResendAttachment(
        [property: JsonPropertyName("filename")] string FileName,
        [property: JsonPropertyName("content")] string Content,
        [property: JsonPropertyName("content_type")] string? ContentType,
        [property: JsonPropertyName("content_id")] string? ContentId);
}
