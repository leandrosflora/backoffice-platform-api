using Backoffice.Application.Approvals;

namespace Backoffice.Api.Approvals;

/// <summary>
/// Maps the approval surface of contracts/openapi/paths/analysis-approval.yaml.
/// </summary>
public static class ApprovalsEndpoints
{
    public static IEndpointRouteBuilder MapApprovalsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/cases/{caseId:guid}/approvals", async (
            Guid caseId,
            HttpRequest httpRequest,
            ListApprovalsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(
                tenantId, caseId, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapPost("/v1/cases/{caseId:guid}/approvals", async (
            Guid caseId,
            DecideApprovalRequest request,
            HttpRequest httpRequest,
            DecideApprovalHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var authorityLimit = RequestContext.GetAuthorityLimit(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(
                tenantId, caseId, request, actorId, roles, subjectType, authorityLimit, correlationId, cancellationToken);
            return Results.Created($"/v1/cases/{caseId}/approvals/{response.ApprovalId}", response);
        });

        return app;
    }
}
