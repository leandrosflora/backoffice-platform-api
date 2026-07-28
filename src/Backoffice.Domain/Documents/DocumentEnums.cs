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
