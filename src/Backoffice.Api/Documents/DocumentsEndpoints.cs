using Backoffice.Application.Documents;

namespace Backoffice.Api.Documents;

/// <summary>
/// Maps the document/evidence surface of contracts/openapi/paths/documents-evidence.yaml.
/// </summary>
public static class DocumentsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases/{caseId:guid}/documents", async (
            Guid caseId,
            RegisterDocumentRequest request,
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
