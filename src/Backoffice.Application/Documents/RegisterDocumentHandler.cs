using System.Security.Cryptography;
using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Evidence;

namespace Backoffice.Application.Documents;

public sealed class RegisterDocumentHandler(
    ICaseRepository caseRepository,
    IDocumentRepository documentRepository,
    IEvidenceRepository evidenceRepository,
    IMalwareScanAdapter malwareScanAdapter,
    IDocumentIntelligenceClient documentIntelligenceClient,
    IUnitOfWork unitOfWork,
    IClock clock,
    PolicyEnforcer policyEnforcer)
{
    public async Task<DocumentResponse> HandleAsync(
        string tenantId,
        Guid caseId,
        long expectedVersion,
        RegisterDocumentRequest request,
        string actorId,
        IReadOnlyList<string> roles,
        string subjectType,
        Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        var @case = await caseRepository.FindByIdAsync(tenantId, caseId, cancellationToken)
            ?? throw new CaseNotFoundException(caseId);

        if (expectedVersion != @case.CaseVersion)
        {
            throw new CaseVersionConflictException(expectedVersion, @case.CaseVersion);
        }

        await policyEnforcer.EnforceAsync(new AuthorizationInput(
            new PolicySubject(actorId, subjectType, tenantId, roles),
            PolicyActions.DocumentRegister,
            new PolicyResource(PolicyResourceTypes.Document, caseId.ToString(), tenantId, @case.State.ToWireString()),
            PolicyPurposes.CaseProcessing,
            correlationId.ToString(),
            new Dictionary<string, object?> { ["case_version"] = expectedVersion }),
            new Dictionary<string, bool> { ["verify-case-version"] = true },
            cancellationToken);

        var checksum = Convert.ToHexStringLower(SHA256.HashData(request.FileContent));
        var storageReference = $"mock://documents/{Guid.NewGuid()}/{request.FileName}";

        var document = Document.Register(
            caseId, tenantId, request.DocumentType, request.MediaType, checksum, storageReference, clock.UtcNow);
        documentRepository.Add(document);

        // Intake acceptance transitions the case regardless of the eventual scan/validation
        // outcome (spec: document-intelligence, "Document validation transitions the case").
        if (@case.State == CaseState.Created)
        {
            @case.Transition(
                @case.CaseVersion, CaseState.DocumentsReceived, EventTypes.DocumentReceived, actorId, "document-intake",
                correlationId, null, "First document registered for the case.", clock.UtcNow);
        }

        var scanResult = await malwareScanAdapter.ScanAsync(document, cancellationToken);
        if (!scanResult.IsClean)
        {
            document.Reject([scanResult.Reason ?? "Malware scan flagged the document."]);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return document.ToResponse();
        }

        document.ClearQuarantine();

        // The intelligence service classifies independently of the client's declared type.
        // Only a non-abstained match corroborates the declaration as evidence. Abstention or
        // disagreement must be handled by a human and cannot satisfy the case's document rules.
        var analysis = await documentIntelligenceClient.AnalyzeAsync(
            request.FileContent, request.FileName, request.MediaType.ToMimeType(), cancellationToken);
        var analysisMatchesDeclaredType = !analysis.Abstained
            && string.Equals(
                analysis.DocumentType,
                request.DocumentType.ToWireString(),
                StringComparison.OrdinalIgnoreCase);

        if (analysisMatchesDeclaredType)
        {
            evidenceRepository.Add(EvidenceRecord.Create(
                caseId, tenantId, EvidenceType.ExtractedField, EvidenceSourceType.Document,
                document.DocumentId.ToString(), document.Version.ToString(), analysis.Confidence,
                value: request.DocumentType.ToString(), checksum: document.Checksum, now: clock.UtcNow));
            document.MarkValidated();
        }
        else
        {
            document.RequireReview();
        }

        if (@case.State == CaseState.DocumentsReceived && document.Status == DocumentStatus.Validated)
        {
            var validatedTypes = (await documentRepository.ListByCaseAsync(tenantId, caseId, cancellationToken))
                .Where(d => d.Status == DocumentStatus.Validated)
                .Select(d => d.DocumentType)
                .Append(document.DocumentType);

            if (DocumentRequirements.AreRequirementsSatisfied(@case.DisputeType, validatedTypes))
            {
                @case.Transition(
                    @case.CaseVersion, CaseState.DocumentsValidated, EventTypes.DocumentValidated, actorId, "document-intake",
                    correlationId, null, "Required documents validated.", clock.UtcNow);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return document.ToResponse();
    }
}
