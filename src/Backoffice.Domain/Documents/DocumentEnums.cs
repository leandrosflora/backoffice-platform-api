namespace Backoffice.Domain.Documents;

public enum DocumentType
{
    Receipt,
    Statement,
    TransactionProof,
    IdentityProof,
    Other,
}

public enum MediaType
{
    ApplicationPdf,
    ImagePng,
    ImageJpeg,
    ApplicationDocx,
    ApplicationXlsx,
}

public static class MediaTypeExtensions
{
    /// <summary>The real MIME type, for forwarding the file to the document-analysis
    /// service — distinct from <c>ToWireString()</c>'s internal SCREAMING_SNAKE_CASE form.</summary>
    public static string ToMimeType(this MediaType mediaType) => mediaType switch
    {
        MediaType.ApplicationPdf => "application/pdf",
        MediaType.ImagePng => "image/png",
        MediaType.ImageJpeg => "image/jpeg",
        MediaType.ApplicationDocx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        MediaType.ApplicationXlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, null),
    };
}

/// <summary>Per contracts/schemas/canonical-models-base.yaml $defs/DocumentStatus.</summary>
public enum DocumentStatus
{
    Received,
    Quarantined,
    Validating,
    Validated,
    Rejected,
}
