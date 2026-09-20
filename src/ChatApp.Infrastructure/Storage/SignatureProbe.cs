using ChatApp.Application.Abstractions;

namespace ChatApp.Infrastructure.Storage;

public sealed class SignatureProbe : IFileSignatureProbe
{
    public string? Detect(string extension, ReadOnlySpan<byte> header)
        => FileSignatureValidator.DetectContentType(extension, header);
}
