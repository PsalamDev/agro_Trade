using AgroTrade.Api.Controllers;
using AgroTrade.Application.Common;
using Microsoft.AspNetCore.Http;

namespace AgroTrade.Tests.Unit;

public sealed class UploadGuardTests
{
    private static IFormFile CreateFile(byte[] bytes, string fileName, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", fileName)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    private static byte[] Pdf() => "%PDF-1.7\n1 0 obj\n<<>>\nendobj\n"u8.ToArray();

    private static byte[] Png() =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private static byte[] Webp() =>
        [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x00];

    private static byte[] Zip() => [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00];

    private static byte[] Ole() =>
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00];

    [Fact]
    public async Task Pdf_IsAcceptedAndDangerousExtensionIsNormalised()
    {
        var file = CreateFile(Pdf(), "licence.html", "application/pdf");

        var (content, fileName, contentType) = await UploadGuard.ReadAsync(file);

        Assert.Equal("licence.pdf", fileName);
        Assert.Equal("application/pdf", contentType);
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer);
        Assert.Equal(Pdf(), buffer.ToArray());
        await content.DisposeAsync();
    }

    [Fact]
    public async Task DeclaredPdfWithPngBytes_IsRejected()
    {
        var file = CreateFile(Png(), "invoice.pdf", "application/pdf");

        var error = await Assert.ThrowsAsync<ApiException>(() => UploadGuard.ReadAsync(file));

        Assert.Equal("FILE_CONTENT_MISMATCH", error.Code);
        Assert.Equal(400, error.StatusCode);
    }

    [Theory]
    [InlineData("image/jpeg", ".jpg", "photo.JPG")]
    [InlineData("image/png", ".png", "photo.png")]
    [InlineData("image/webp", ".webp", "photo.webp")]
    public async Task ImagesAreAccepted(string contentType, string expectedExtension, string fileName)
    {
        var bytes = contentType switch
        {
            "image/jpeg" => Jpeg(),
            "image/png" => Png(),
            "image/webp" => Webp(),
            _ => throw new InvalidOperationException(),
        };

        var (content, storedName, _) = await UploadGuard.ReadAsync(
            CreateFile(bytes, fileName, contentType));

        Assert.EndsWith(expectedExtension, storedName);
        await content.DisposeAsync();
    }

    [Theory]
    [InlineData("application/msword", ".doc", "contract.doc")]
    [InlineData("application/vnd.ms-excel", ".xls", "prices.xls")]
    public async Task LegacyOfficeDocumentsUseTheOleSignature(
        string contentType, string expectedExtension, string fileName)
    {
        var (content, storedName, _) = await UploadGuard.ReadAsync(
            CreateFile(Ole(), fileName, contentType));

        Assert.EndsWith(expectedExtension, storedName);
        await content.DisposeAsync();
    }

    [Theory]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", ".docx", "contract.docx")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ".xlsx", "prices.xlsx")]
    public async Task ZipBasedOfficeDocumentsUseTheZipSignature(
        string contentType, string expectedExtension, string fileName)
    {
        var (content, storedName, _) = await UploadGuard.ReadAsync(
            CreateFile(Zip(), fileName, contentType));

        Assert.EndsWith(expectedExtension, storedName);
        await content.DisposeAsync();
    }

    [Fact]
    public async Task CsvWithCharsetParameter_IsAccepted()
    {
        var file = CreateFile(
            "name,country\nAda,NG\n"u8.ToArray(),
            "buyers.csv",
            "text/csv; charset=utf-8");

        var (content, storedName, contentType) = await UploadGuard.ReadAsync(file);

        Assert.Equal("buyers.csv", storedName);
        Assert.Equal("text/csv", contentType);
        await content.DisposeAsync();
    }

    [Fact]
    public async Task BinaryFileDeclaredAsCsv_IsRejected()
    {
        var file = CreateFile([0x61, 0x2C, 0x62, 0x00, 0x63], "buyers.csv", "text/csv");

        var error = await Assert.ThrowsAsync<ApiException>(() => UploadGuard.ReadAsync(file));

        Assert.Equal("FILE_CONTENT_MISMATCH", error.Code);
    }

    [Fact]
    public async Task UnsupportedContentType_IsRejected()
    {
        var file = CreateFile(
            "<html><script>alert(1)</script></html>"u8.ToArray(),
            "page.html",
            "text/html");

        var error = await Assert.ThrowsAsync<ApiException>(() => UploadGuard.ReadAsync(file));

        Assert.Equal("FILE_TYPE_NOT_ALLOWED", error.Code);
    }

    [Fact]
    public async Task EmptyFile_IsRejected()
    {
        var file = CreateFile([], "empty.pdf", "application/pdf");

        var error = await Assert.ThrowsAsync<ApiException>(() => UploadGuard.ReadAsync(file));

        Assert.Equal("EMPTY_FILE", error.Code);
    }
}
