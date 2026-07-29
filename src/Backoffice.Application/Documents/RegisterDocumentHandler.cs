using Backoffice.Application.Abstractions;
using Backoffice.Application.Cases;
using Backoffice.Application.Policy;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;

namespace Backoffice.Application.Documents;

/// <summary>
/// Accepts an upload only after it is durably stored in quarantine. Expensive and fallible
/// scanning/analysis runs in <see cref="ProcessDocumentHandler"/> through a worker in
/// deployed environments.
/// </summary>
public sealed class RegisterDocumentHandler(
    ICaseRepository caseRepository,
    IDocumentRepository documentRepository,
    IDocumentStorage documentStorage,
    ProcessDocumentHandler processDocumentHandler,
    DocumentProcessingOptions processingOptions,
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

        var stored = await documentStorage.StoreQuarantinedAsync(
            tenantId, caseId, request.FileName, request.FileContent, cancellationToken);

        var document = Document.Register(
            caseId,
            tenantId,
            request.DocumentType,
            request.MediaType,
            stored.Checksum,
            stored.StorageReference,
            clock.UtcNow);
        documentRepository.Add(document);

        if (@case.State == CaseState.Created)
        {
            @case.Transition(
                @case.CaseVersion,
                CaseState.DocumentsReceived,
                EventTypes.DocumentReceived,
                actorId,
                "document-intake",
                correlationId,
                null,
                "First document registered for the case.",
                clock.UtcNow);
        }

        // Commit the intake before any external dependency is called. A scanner or AI
        // outage can therefore be retried from durable QUARANTINED/VALIDATING state.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!processingOptions.Inline)
        {
            return document.ToResponse();
        }

        return await processDocumentHandler.HandleAsync(
            tenantId, caseId, document.DocumentId, actorId, correlationId, cancellationToken);
    }
}
