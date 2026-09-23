using AgroTrade.Application.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Infrastructure.Storage;

/// <summary>
/// Local-disk file storage (dev). Writes under wwwroot/uploads and returns a
/// relative URL served by static files. Cloud adapters replace this later (plan §04).
/// </summary>
public sealed class LocalFileStorage(
    IWebHostEnvironment env,
    ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly string _root = Path.Combine(
        env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"), "uploads");

    public Task<StoredFile> SaveAsync(Stream content, string fileName, string contentType, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var safe = ext.Length > 0 && ext.Length <= 10 ? ext : string.Empty;
        var storedName = $"{Guid.NewGuid():N}{safe}";
        var fullPath = Path.Combine(_root, storedName);

        Directory.CreateDirectory(_root);

        long sizeBytes;
        using (var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write))
        {
            content.CopyTo(file);
            sizeBytes = file.Length;
        }

        logger.LogInformation("Stored file {Name} ({Bytes} bytes) -> {Path}", storedName, sizeBytes, fullPath);
        return Task.FromResult(new StoredFile($"/uploads/{storedName}", fileName, contentType, sizeBytes));
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        const string prefix = "/uploads/";
        var relative = url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? url[prefix.Length..]
            : url.TrimStart('/');

        if (relative.Contains("..", StringComparison.Ordinal) || relative.Contains('/') || relative.Contains('\\'))
        {
            // Only bare filenames may be deleted — never a traversal path.
            return Task.CompletedTask;
        }

        var fullPath = Path.Combine(_root, relative);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            logger.LogInformation("Deleted file {Path}", fullPath);
        }
        return Task.CompletedTask;
    }
}