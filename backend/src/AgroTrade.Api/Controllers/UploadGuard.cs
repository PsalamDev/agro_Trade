using AgroTrade.Application.Common;

namespace AgroTrade.Api.Controllers;

/// <summary>
/// Guards file uploads per plan §03: ≤ 10 MB, allowlisted document/image MIME
/// types. Returns an open stream for the storage service to consume.
/// </summary>
public static class UploadGuard
{
    public const int MaxUploadBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> Allowlisted = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "text/csv",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Validates size + MIME type and yields a readable stream.</summary>
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
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;
        if (!Allowlisted.Contains(contentType))
        {
            throw ApiException.BadRequest("FILE_TYPE_NOT_ALLOWED", "That file type is not allowed (PDF, images, Office docs, CSV allowed).");
        }

        var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        return (ms, Path.GetFileName(file.FileName), contentType);
    }
}