using Backoffice.Application.Recommendations;

namespace Backoffice.Api.Recommendations;

/// <summary>
/// Maps the recommendation surface of contracts/openapi/paths/analysis-approval.yaml.
/// </summary>
public static class RecommendationsEndpoints
{
    public static IEndpointRouteBuilder MapRecommendationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases/{caseId:guid}/recommendations", async (
            Guid caseId,
            CreateRecommendationRequest request,
            HttpRequest httpRequest,
            CreateRecommendationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, caseId, request, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Created($"/v1/cases/{caseId}/recommendations/{response.RecommendationId}", response);
        });

        return app;
    }
}
