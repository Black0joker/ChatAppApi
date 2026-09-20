namespace ChatApp.Application.Abstractions;

/// <summary>Server-side content sniffing (PLAN §30): magic bytes over client claims.</summary>
public interface IFileSignatureProbe
{
    /// <returns>Server-determined content type, or null when the bytes don't match the extension.</returns>
    string? Detect(string extension, ReadOnlySpan<byte> header);
}
