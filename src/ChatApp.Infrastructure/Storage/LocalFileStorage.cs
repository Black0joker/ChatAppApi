using ChatApp.Application.Abstractions;
using ChatApp.Application.Common;
using Microsoft.Extensions.Options;

namespace ChatApp.Infrastructure.Storage;

/// <summary>
/// Filesystem provider for single-instance/dev. Production object storage (S3)
/// slots into <see cref="IFileStorage"/> without touching callers.
/// Files are only ever served through the authorized download endpoint.
/// </summary>
public sealed class LocalFileStorage(IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = ResolveRoot(options.Value.LocalPath);

    public async Task<StoredFile> SaveAsync(Stream content, string storageKeySuffix, CancellationToken ct = default)
    {
        var key = $"{DateTimeOffset.UtcNow:yyyy/MM/dd}/{Guid.NewGuid():N}{storageKeySuffix}";
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        await file.FlushAsync(ct);
        return new StoredFile(key, file.Length);
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        if (!File.Exists(path))
            throw new FileNotFoundException("Stored file not found.", storageKey);
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    public Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var path = ResolvePath(storageKey);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    private string ResolvePath(string storageKey)
    {
        // Forward-slash keys, OS-correct paths, no directory escape.
        var relative = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(_root, relative));
        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Storage key escapes the root directory.");
        return full;
    }

    private static string ResolveRoot(string configured)
        => System.IO.Path.GetFullPath(
            System.IO.Path.IsPathRooted(configured)
                ? configured
                : System.IO.Path.Combine(AppContext.BaseDirectory, configured));
}
