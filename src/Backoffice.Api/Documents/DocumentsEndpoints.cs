using Backoffice.Application.Documents;
using Backoffice.Application.Policy;
using Backoffice.Domain.Documents;

namespace Backoffice.Api.Documents;

/// <summary>
/// Maps the document/evidence surface of contracts/openapi/paths/documents-evidence.yaml.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        // multipart/form-data, not a JSON body — the real file now has to reach the
        // document-analysis service, not just a mock storage-reference string (spec:
        // document-intelligence).
        app.MapPost("/v1/cases/{caseId:guid}/documents", async (
            Guid caseId,
            HttpRequest httpRequest,
            RegisterDocumentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);
            var expectedVersion = RequestContext.RequireIfMatch(httpRequest);

            var form = await httpRequest.ReadFormAsync(cancellationToken);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { detail = "Missing 'file' field." });
            }

            var documentType = PolicyWireFormat.FromWireString<DocumentType>(form["documentType"].ToString());
            var mediaType = PolicyWireFormat.FromWireString<MediaType>(form["mediaType"].ToString());

            await using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);

            var request = new RegisterDocumentRequest(documentType, mediaType, memoryStream.ToArray(), file.FileName);

            var response = await handler.HandleAsync(
                tenantId, caseId, expectedVersion, request, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Accepted($"/v1/cases/{caseId}/documents/{response.DocumentId}", response);
        });

        app.MapGet("/v1/cases/{caseId:guid}/documents/{documentId:guid}", async (
            Guid caseId,
            Guid documentId,
            HttpRequest httpRequest,
            GetDocumentHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, caseId, documentId, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapGet("/v1/cases/{caseId:guid}/evidence", async (
            Guid caseId,
            HttpRequest httpRequest,
            ListEvidenceHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, caseId, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}
