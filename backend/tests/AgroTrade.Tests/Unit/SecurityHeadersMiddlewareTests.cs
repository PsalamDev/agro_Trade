using AgroTrade.Api.Middleware;
using Microsoft.AspNetCore.Http;

namespace AgroTrade.Tests.Unit;

public sealed class SecurityHeadersMiddlewareTests
{
    private static DefaultHttpContext Context(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        return context;
    }

    private static SecurityHeadersMiddleware Middleware() => new(_ => Task.CompletedTask);

    [Fact]
    public async Task ApiResponses_AreLockedDown()
    {
        var context = Context("/api/v1/orders");

        await Middleware().InvokeAsync(context);

        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("no-referrer", context.Response.Headers["Referrer-Policy"].ToString());
        Assert.Contains(
            "default-src 'none'",
            context.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Fact]
    public async Task NonImageUploads_AreForcedToDownload()
    {
        var context = Context("/uploads/licence.pdf");

        await Middleware().InvokeAsync(context);

        Assert.Equal("attachment", context.Response.Headers["Content-Disposition"].ToString());
        Assert.Contains("sandbox", context.Response.Headers["Content-Security-Policy"].ToString());
    }

    [Theory]
    [InlineData("/uploads/photo.jpg")]
    [InlineData("/uploads/photo.jpeg")]
    [InlineData("/uploads/photo.png")]
    [InlineData("/uploads/photo.webp")]
    public async Task ProductImages_StayInline(string path)
    {
        var context = Context(path);

        await Middleware().InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("Content-Disposition"));
    }

    [Fact]
    public async Task OtherRoutes_DoNotGetUploadHeaders()
    {
        var context = Context("/health");

        await Middleware().InvokeAsync(context);

        Assert.False(context.Response.Headers.ContainsKey("Content-Disposition"));
        Assert.False(context.Response.Headers.ContainsKey("Content-Security-Policy"));
    }
}
