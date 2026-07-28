namespace Backoffice.Domain.Documents;

/// <summary>
/// Deterministic, rule-based classification signal derived from the document's storage
/// reference (filename), used to corroborate the caller-declared DocumentType with an
/// evidence confidence score. A stand-in for the real OCR/content classifier described in
/// docs/architecture/component-document-intelligence.md — deterministic and abstaining by
/// design, per the eval guardrail unknown_document_abstention_rate: 1.0.
/// </summary>
public static class DocumentClassifier
{
    private static readonly Dictionary<DocumentType, string[]> Keywords = new()
    {
        [DocumentType.Receipt] = ["receipt", "recibo", "comprovante-compra"],
        [DocumentType.Statement] = ["statement", "extrato"],
        [DocumentType.TransactionProof] = ["transaction", "transacao", "comprovante-transacao", "pix", "transferencia"],
        [DocumentType.IdentityProof] = ["identity", "identidade", "rg", "cpf-doc"],
    };

    /// <summary>
    /// Returns a classification confidence in [0,1] for how strongly the storage reference's
    /// filename corroborates <paramref name="declaredType"/>. Returns null (abstain) when the
    /// filename contains no recognizable signal at all, rather than guessing a default score.
    /// </summary>
    public static double? TryScoreAgainstDeclaredType(string storageReference, DocumentType declaredType)
    {
        var normalized = storageReference.ToLowerInvariant();
        var anySignalFound = Keywords.Values.Any(kws => kws.Any(normalized.Contains));

        if (!anySignalFound)
        {
            return null;
        }

        if (Keywords.TryGetValue(declaredType, out var declaredKeywords) && declaredKeywords.Any(normalized.Contains))
        {
            return 0.9;
        }

        return 0.3;
    }
}
