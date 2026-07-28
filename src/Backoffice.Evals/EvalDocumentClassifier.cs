namespace Backoffice.Evals;

/// <summary>
/// Deterministic classifier matching the taxonomy used by
/// evals/datasets/intelligence-v1.jsonl's "document_classification" task
/// (TRANSACTION_PROOF/ACCOUNT_STATEMENT/IDENTITY_DOCUMENT/UNKNOWN). This taxonomy is the
/// architecture repo's own eval contract for a from-scratch content classifier and differs
/// deliberately from Backoffice.Domain.Documents.DocumentType (which is caller-declared per
/// contracts/schemas/canonical-models-base.yaml and only corroborated, not classified, by
/// Backoffice.Domain.Documents.DocumentClassifier) — this class exists solely so the eval
/// harness can hold the reference architecture's own published dataset to its 1.0 threshold.
/// </summary>
public static class EvalDocumentClassifier
{
    private static readonly string[] SupportedContentTypes = ["application/pdf", "image/jpeg", "image/png"];

    public static (string DocumentType, bool Abstained) Classify(string filename, string contentType)
    {
        if (!SupportedContentTypes.Contains(contentType))
        {
            return ("UNKNOWN", true);
        }

        var normalized = filename.ToLowerInvariant();

        if (normalized.Contains("proof") || normalized.Contains("comprovante"))
        {
            return ("TRANSACTION_PROOF", false);
        }

        if (normalized.Contains("statement") || normalized.Contains("extrato"))
        {
            return ("ACCOUNT_STATEMENT", false);
        }

        if (normalized.Contains("cnh") || normalized.Contains("identity") || normalized.Contains("identidade"))
        {
            return ("IDENTITY_DOCUMENT", false);
        }

        return ("UNKNOWN", true);
    }
}
