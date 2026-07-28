using Backoffice.Application.Investigations;

namespace Backoffice.Api.Investigations;

/// <summary>
/// Maps the investigation surface of contracts/openapi/paths/analysis-approval.yaml.
/// </summary>
public static class InvestigationsEndpoints
{
    public static IEndpointRouteBuilder MapInvestigationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases/{caseId:guid}/investigations", async (
            Guid caseId,
            StartInvestigationRequest request,
            HttpRequest httpRequest,
            StartInvestigationHandler handler,
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
            return Results.Accepted($"/v1/cases/{caseId}/investigations/{response.InvestigationId}", response);
        });

        return app;
    }
}
