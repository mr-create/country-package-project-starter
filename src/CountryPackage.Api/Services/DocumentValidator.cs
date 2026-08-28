using System.Security.Cryptography;

namespace CountryPackage.Api.Services;

public sealed record ValidatedDocument(string FileName, string ContentType, byte[] Content, string Sha256);

public sealed class DocumentValidator(IConfiguration configuration)
{
    private const string PdfContentType = "application/pdf";
    private const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private readonly long _maximumBytes = configuration.GetValue<long?>("Storage:MaximumUploadBytes") ?? 10 * 1024 * 1024;

    public async Task<ValidatedDocument> ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
            throw new ApiException(400, "document.empty", "The uploaded document is empty.");
        if (file.Length > _maximumBytes)
            throw new ApiException(413, "document.too_large", "The uploaded document exceeds the 10 MB limit.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var expectedContentType = extension switch
        {
            ".pdf" => PdfContentType,
            ".docx" => DocxContentType,
            _ => throw new ApiException(415, "document.unsupported_type", "Only PDF and DOCX documents are accepted.")
        };

        if (!string.Equals(file.ContentType, expectedContentType, StringComparison.OrdinalIgnoreCase))
            throw new ApiException(415, "document.content_type_mismatch", "The file extension and content type do not match.");

        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);
        var content = memory.ToArray();

        var signatureValid = extension == ".pdf"
            ? content.AsSpan().StartsWith("%PDF"u8)
            : content.Length >= 4 && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04;

        if (!signatureValid)
            throw new ApiException(415, "document.signature_mismatch", "The file signature does not match its declared type.");

        var safeName = Path.GetFileName(file.FileName);
        return new(safeName, expectedContentType, content, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }
}
