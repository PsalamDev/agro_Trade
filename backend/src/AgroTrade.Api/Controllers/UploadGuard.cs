using AgroTrade.Application.Common;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Guards file uploads per plan §03: ≤ 10 MB, allowlisted document/image MIME
/// types, and a content signature that matches the declared type. Returns an open stream for
/// the storage service to consume.
/// </summary>
/// <remarks>
/// A MIME type is supplied by the client and proves nothing. The signature check is what stops
/// an HTML file declared as a PDF from being stored as an HTML page, and the extension is then
/// normalised to the canonical one for the declared type so the static-file server cannot pick
/// an executable content type from a user-chosen name.
/// </remarks>
public static class UploadGuard
{
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly Dictionary<string, string> CanonicalExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["application/pdf"] = ".pdf",
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
        ["application/msword"] = ".doc",
        ["application/vnd.openxmlformats-officedocument.wordprocessingml.document"] = ".docx",
        ["text/csv"] = ".csv",
        ["application/vnd.ms-excel"] = ".xls",
        ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] = ".xlsx",
    };

    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] Utf16LeBom = [0xFF, 0xFE];
    private static readonly byte[] Utf16BeBom = [0xFE, 0xFF];

    /// <summary>Validates size, MIME type and file signature, then yields a readable stream.</summary>
    public static async Task<(Stream Content, string FileName, string ContentType)> ReadAsync(IFormFile file, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0)
        {
            throw ApiException.BadRequest("EMPTY_FILE", "A non-empty file is required.");
        }
        if (file.Length > MaxUploadBytes)
        {
            throw ApiException.BadRequest("FILE_TOO_LARGE", "Files must be 10 MB or smaller.");
        }

        // Content-Type may carry parameters, e.g. "text/csv; charset=utf-8".
        var contentType = (file.ContentType ?? string.Empty).Split(';', 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(contentType) ||
            !CanonicalExtensions.TryGetValue(contentType, out var canonicalExtension))
        {
            throw ApiException.BadRequest(
                "FILE_TYPE_NOT_ALLOWED",
                "That file type is not allowed (PDF, images, Office docs, CSV allowed).");
        }

        var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);

        if (!MatchesDeclaredType(contentType, ms.GetBuffer().AsSpan(0, (int)ms.Length)))
        {
            throw ApiException.BadRequest(
                "FILE_CONTENT_MISMATCH",
                "The file content does not match its declared type.");
        }

        // Keep the display name but replace the extension with the canonical one. A file named
        // "licence.html" that genuinely starts with %PDF- is stored as "licence.pdf".
        var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var baseName = Path.GetFileNameWithoutExtension(originalName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "upload";
        }

        ms.Position = 0;
        return (ms, baseName + canonicalExtension, contentType);
    }

    private static bool MatchesDeclaredType(string contentType, ReadOnlySpan<byte> content) =>
        contentType.ToLowerInvariant() switch
        {
            "application/pdf" => content.StartsWith("%PDF-"u8),
            "image/jpeg" => content.StartsWith(JpegSignature),
            "image/png" => content.StartsWith(PngSignature),
            "image/webp" => content.Length >= 12 &&
                            content[..4].SequenceEqual("RIFF"u8) &&
                            content.Slice(8, 4).SequenceEqual("WEBP"u8),
            "application/msword" or "application/vnd.ms-excel" => content.StartsWith(OleSignature),
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                or "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => IsZip(content),
            "text/csv" => IsText(content),
            _ => false,
        };

    private static bool IsZip(ReadOnlySpan<byte> content) =>
        content.Length >= 4 &&
        content[0] == 0x50 &&
        content[1] == 0x4B &&
        ((content[2] == 0x03 && content[3] == 0x04) ||
         (content[2] == 0x05 && content[3] == 0x06) ||
         (content[2] == 0x07 && content[3] == 0x08));

    private static bool IsText(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith(Utf16LeBom) || content.StartsWith(Utf16BeBom))
        {
            return true;
        }

        if (content.StartsWith(Utf8Bom))
        {
            content = content[Utf8Bom.Length..];
        }

        // A text file has no NUL bytes. This is deliberately permissive on encoding: CSV is
        // often exported in a local code page, and the static file server serves it as a
        // download rather than as a page.
        return !content.Contains((byte)0);
    }
}
