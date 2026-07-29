using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Evidence;
using System.Security.Cryptography;

namespace Backoffice.Application.Documents;

/// <summary>
/// Restartable document pipeline. QUARANTINED and VALIDATING are both processable so a
/// worker crash or dependency outage can resume without accepting an unscanned file.
/// </summary>
public sealed class ProcessDocumentHandler(
    ICaseRepository caseRepository,
    IDocumentRepository documentRepository,
    IEvidenceRepository evidenceRepository,
    IDocumentStorage documentStorage,
    IMalwareScanAdapter malwareScanAdapter,
    IDocumentIntelligenceClient documentIntelligenceClient,
    IUnitOfWork unitOfWork,
    IClock clock)
{
    public async Task<DocumentResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        Guid documentId,
        string actorId,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var document = await documentRepository.FindByIdAsync(tenantId, caseId, documentId, cancellationToken)
            ?? throw new DocumentNotFoundException(documentId);

        if (document.Status is not (DocumentStatus.Quarantined or DocumentStatus.Validating))
        {
            return document.ToResponse();
        }

        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (document.Status == DocumentStatus.Quarantined)
        {
            document.ClearQuarantine();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        var storedDocument = await documentStorage.ReadAsync(document.StorageReference, cancellationToken);
        var content = storedDocument.Content;
        var storedChecksum = Convert.ToHexStringLower(SHA256.HashData(content));
        if (!string.Equals(storedChecksum, document.Checksum, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Stored content checksum does not match document {document.DocumentId} metadata.");
        }

        var scanResult = await malwareScanAdapter.ScanAsync(document, content, cancellationToken);
        if (!scanResult.IsClean)
        {
            document.Reject([scanResult.Reason ?? "Malware scan rejected the document."]);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return document.ToResponse();
        }

        var analysis = await documentIntelligenceClient.AnalyzeAsync(
            content, storedDocument.FileName, document.MediaType.ToMimeType(), cancellationToken);
        var analysisMatchesDeclaredType = !analysis.Abstained
            && string.Equals(
                analysis.DocumentType,
                document.DocumentType.ToWireString(),
                StringComparison.OrdinalIgnoreCase);

        // Promotion is idempotent. The quarantine copy remains available until a separate
        // retention cleanup, avoiding data loss if the database commit fails after copying.
        var acceptedStorageReference = await documentStorage.PromoteAsync(
            document.StorageReference, cancellationToken);

        if (analysisMatchesDeclaredType)
        {
            evidenceRepository.Add(EvidenceRecord.Create(
                caseId,
                tenantId,
                EvidenceType.ExtractedField,
                EvidenceSourceType.Document,
                document.DocumentId.ToString(),
                document.Version.ToString(),
                analysis.Confidence,
                value: document.DocumentType.ToString(),
                checksum: document.Checksum,
                now: clock.UtcNow));
            document.MarkValidated(acceptedStorageReference);
        }
        else
        {
            document.RequireReview(acceptedStorageReference);
        }

        if (@case.State == CaseState.DocumentsReceived && document.Status == DocumentStatus.Validated)
        {
            var validatedTypes = (await documentRepository.ListByCaseAsync(tenantId, caseId, cancellationToken))
                .Where(candidate => candidate.Status == DocumentStatus.Validated)
                .Select(candidate => candidate.DocumentType);

            if (DocumentRequirements.AreRequirementsSatisfied(@case.DisputeType, validatedTypes))
            {
                @case.Transition(
                    @case.CaseVersion,
                    CaseState.DocumentsValidated,
                    EventTypes.DocumentValidated,
                    actorId,
                    "document-processing",
                    correlationId,
                    null,
                    "Required documents validated.",
                    clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return document.ToResponse();
    }
}
