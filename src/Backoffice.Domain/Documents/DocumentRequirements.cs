using Backoffice.Domain.Cases;

namespace Backoffice.Domain.Documents;

/// <summary>
/// Deterministic mapping of which document types must reach VALIDATED before a case can
/// advance to DOCUMENTS_VALIDATED. A concrete, documented stand-in for the full BR-002..
/// BR-005 requirement tables until those are transcribed in detail from the architecture
/// docs — every dispute type requires at least one validated document of the listed types.
/// </summary>
public static class DocumentRequirements
{
    private static readonly Dictionary<DisputeType, DocumentType[]> RequiredTypes = new()
    {
        [DisputeType.CardPurchase] = [DocumentType.Receipt],
        [DisputeType.Pix] = [DocumentType.TransactionProof],
        [DisputeType.Transfer] = [DocumentType.TransactionProof],
        [DisputeType.CashWithdrawal] = [DocumentType.TransactionProof],
        [DisputeType.Other] = [DocumentType.Receipt, DocumentType.Statement, DocumentType.TransactionProof, DocumentType.IdentityProof, DocumentType.Other],
    };

    /// <summary>True when at least one validated document matches a required type for the dispute type.</summary>
    public static bool AreRequirementsSatisfied(DisputeType disputeType, IEnumerable<DocumentType> validatedDocumentTypes)
    {
        var required = RequiredTypes[disputeType];
        var validatedSet = validatedDocumentTypes.ToHashSet();
        return required.Any(validatedSet.Contains);
    }
}
