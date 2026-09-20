namespace ChatApp.Infrastructure.Storage;

/// <summary>
/// Server-side file validation (PLAN §18/§30): never trust client-provided
/// content-type, extension, or size. Magic bytes must match the extension and
/// resolve to a known safe content type.
/// </summary>
public static class FileSignatureValidator
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] Riff = "RIFF"u8.ToArray();
    private static readonly byte[] Webp = "WEBP"u8.ToArray();
    private static readonly byte[] Pdf = "%PDF"u8.ToArray();

    /// <returns>Server-determined content type, or null when rejected.</returns>
    public static string? DetectContentType(string extension, ReadOnlySpan<byte> header)
    {
        return extension switch
        {
            ".png" when StartsWith(header, Png) => "image/png",
            ".jpg" or ".jpeg" when StartsWith(header, Jpeg) => "image/jpeg",
            ".gif" when StartsWith(header, Gif87) || StartsWith(header, Gif89) => "image/gif",
            ".webp" when StartsWith(header, Riff) && header.Length >= 12 && StartsWith(header[8..], Webp) => "image/webp",
            ".pdf" when StartsWith(header, Pdf) => "application/pdf",
            ".txt" when IsText(header) => "text/plain; charset=utf-8",
            _ => null
        };
    }

    private static bool StartsWith(ReadOnlySpan<byte> data, byte[] prefix)
        => data.Length >= prefix.Length && data[..prefix.Length].SequenceEqual(prefix);

    // Plain text only: reject control bytes (except tab/LF/CR) to block binaries renamed .txt.
    private static bool IsText(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            if (b < 0x09 || (b > 0x0D && b < 0x20) || b == 0x7F)
                return false;
        return true;
    }
}
