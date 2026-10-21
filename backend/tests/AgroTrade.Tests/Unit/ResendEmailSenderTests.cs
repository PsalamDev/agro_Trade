using System.Net;
using System.Text.Json;
using AgroTrade.Application.Common;
using AgroTrade.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgroTrade.Tests.Unit;

/// <summary>
/// The Resend transport is the one that actually reaches the customer, so its wire format is
/// pinned here: the endpoint, the bearer token, the field names the provider expects, and the
/// inline brand mark travelling as an attachment with a content id the HTML can reference.
/// </summary>
public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task SendAsync_PostsTheMessageToTheResendApi()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, """{"id":"email-123"}"""));
        var sender = Sender(handler);

        await sender.SendAsync(Message());

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/emails", request.Path);
        Assert.Equal("re_test_key", request.ApiKey);
        Assert.False(string.IsNullOrWhiteSpace(request.IdempotencyKey));

        using var body = JsonDocument.Parse(request.Body!);
        var root = body.RootElement;
        Assert.Equal(
            "Achievers Global Consult <no-reply@achieversglobalconsult.com>",
            root.GetProperty("from").GetString());
        Assert.Equal("ada@example.com", root.GetProperty("to")[0].GetString());
        Assert.Equal("Confirm your email", root.GetProperty("subject").GetString());
        Assert.Equal("<p>Hello</p>", root.GetProperty("html").GetString());
        Assert.Equal("Hello", root.GetProperty("text").GetString());
        Assert.False(root.TryGetProperty("attachments", out _));
    }

    [Fact]
    public async Task SendAsync_CarriesTheBrandMarkAsAnInlineAttachment()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, """{"id":"email-123"}"""));
        var sender = Sender(handler);

        await sender.SendAsync(Message(
            html: "<img src=\"cid:achievers-brand-mark\" />",
            inlineImages:
            [
                new EmailInlineImage("achievers-brand-mark", "brand-mark.png", "image/png", png),
            ]));

        var request = Assert.Single(handler.Requests);
        using var body = JsonDocument.Parse(request.Body!);
        var attachment = body.RootElement.GetProperty("attachments")[0];
        Assert.Equal("brand-mark.png", attachment.GetProperty("filename").GetString());
        Assert.Equal("achievers-brand-mark", attachment.GetProperty("content_id").GetString());
        Assert.Equal("image/png", attachment.GetProperty("content_type").GetString());
        Assert.Equal(Convert.ToBase64String(png), attachment.GetProperty("content").GetString());
    }

    [Fact]
    public async Task SendAsync_WhenTheProviderRejects_TheMessage_FailsWithoutRetrying()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.UnprocessableEntity, """{"message":"The from address is not verified"}"""));
        var sender = Sender(handler, maxRetries: 2);

        var error = await Assert.ThrowsAsync<ResendDeliveryException>(() => sender.SendAsync(Message()));

        Assert.Contains("not verified", error.Message);
        // A rejection is a decision, not an outage: repeating it would only burn quota.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_WhenTheProviderIsRateLimited_RetriesWithTheSameIdempotencyKey()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.TooManyRequests, """{"message":"too many requests"}"""),
            Response(HttpStatusCode.OK, """{"id":"email-123"}"""));
        var sender = Sender(handler, maxRetries: 2);

        await sender.SendAsync(Message());

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].IdempotencyKey, handler.Requests[1].IdempotencyKey);
    }

    [Fact]
    public async Task SendAsync_WhenEveryAttemptFails_FailsAfterTheConfiguredRetries()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.ServiceUnavailable, """{"message":"upstream unavailable"}"""),
            Response(HttpStatusCode.ServiceUnavailable, """{"message":"upstream unavailable"}"""),
            Response(HttpStatusCode.ServiceUnavailable, """{"message":"upstream unavailable"}"""));
        var sender = Sender(handler, maxRetries: 2);

        var error = await Assert.ThrowsAsync<ResendDeliveryException>(() => sender.SendAsync(Message()));

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("after 3 attempt", error.Message);
    }

    [Fact]
    public async Task SendAsync_WithoutAnApiKey_FailsBeforeCallingTheProvider()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, "{}"));
        var settings = DefaultOptions();
        settings.Resend.ApiKey = null;
        var sender = Sender(handler, settings);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendAsync(Message()));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendAsync_NeverPutsTheApiKeyInTheFailure()
    {
        var handler = new RecordingHandler(
            Response(HttpStatusCode.UnprocessableEntity, """{"message":"invalid api key"}"""));
        var sender = Sender(handler);

        var error = await Assert.ThrowsAsync<ResendDeliveryException>(() => sender.SendAsync(Message()));

        Assert.DoesNotContain("re_test_key", error.Message);
    }

    [Fact]
    public async Task SendAsync_WhenTheCallerCancels_DoesNotSwallowTheCancellation()
    {
        var handler = new RecordingHandler(Response(HttpStatusCode.OK, "{}"));
        var sender = Sender(handler);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => sender.SendAsync(Message(), cts.Token));
    }

    private static EmailMessage Message(
        string html = "<p>Hello</p>",
        IReadOnlyList<EmailInlineImage>? inlineImages = null) => new(
        To: "ada@example.com",
        Subject: "Confirm your email",
        HtmlBody: html,
        PlainTextBody: "Hello",
        InlineImages: inlineImages);

    private static ResendEmailSender Sender(
        HttpMessageHandler handler, EmailOptions? settings = null, int maxRetries = 0)
    {
        settings ??= DefaultOptions(maxRetries);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.resend.test/") };

        return new ResendEmailSender(
            client,
            Options.Create(settings),
            NullLogger<ResendEmailSender>.Instance);
    }

    private static EmailOptions DefaultOptions(int maxRetries = 0) => new()
    {
        FromAddress = "no-reply@achieversglobalconsult.com",
        FromName = "Achievers Global Consult",
        Resend = new EmailOptions.ResendSettings
        {
            ApiKey = "re_test_key",
            BaseUrl = "https://api.resend.test",
            MaxRetries = maxRetries,
            RetryBaseDelayMilliseconds = 1,
        },
    };

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body) };

    private sealed record CapturedRequest(
        HttpMethod Method,
        string Path,
        string? ApiKey,
        string? IdempotencyKey,
        string? Body);

    /// <summary>
    /// Hands out the queued responses one per call, repeating the last one so a test can assert
    /// how many attempts were actually made.
    /// </summary>
    private sealed class RecordingHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.Parameter,
                request.Headers.TryGetValues("Idempotency-Key", out var keys)
                    ? keys.FirstOrDefault()
                    : null,
                body));

            return _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
        }
    }
}
