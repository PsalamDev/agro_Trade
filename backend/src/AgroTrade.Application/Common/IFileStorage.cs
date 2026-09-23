namespace AgroTrade.Application.Common;

/// <summary>
/// Storage abstraction for business documents and product images. Local disk in dev,
/// cloud (Cloudinary / S3) later without touching callers (plan §04).
/// </summary>
public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default);

    Task DeleteAsync(string url, CancellationToken ct = default);
}

public sealed record StoredFile(string Url, string FileName, string ContentType, long SizeBytes);