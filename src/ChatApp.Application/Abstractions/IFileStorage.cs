namespace ChatApp.Application.Abstractions;

/// <summary>Object/file storage provider abstraction (PLAN §18, §36).</summary>
public sealed record StoredFile(string StorageKey, long Size);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string storageKeySuffix, CancellationToken ct = default);
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct = default);
    Task DeleteAsync(string storageKey, CancellationToken ct = default);
}
