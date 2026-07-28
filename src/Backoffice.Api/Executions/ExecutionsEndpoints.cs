using Backoffice.Application.Executions;

namespace Backoffice.Api.Executions;

/// <summary>
/// Maps the execution/reconciliation surface of contracts/openapi/paths/execution-audit.yaml.
/// </summary>
public static class ExecutionsEndpoints
{
    public static IEndpointRouteBuilder MapExecutionsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases/{caseId:guid}/executions", async (
            Guid caseId,
            RequestExecutionRequest request,
            HttpRequest httpRequest,
            RequestExecutionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);
            var idempotencyKey = RequestContext.RequireIdempotencyKey(httpRequest);

            var result = await handler.HandleAsync(
                tenantId, caseId, idempotencyKey, request, actorId, roles, subjectType, correlationId, cancellationToken);

            // 200 for an idempotent replay, 202 for a freshly accepted execution (contract:
            // contracts/openapi/paths/execution-audit.yaml).
            return result.IsReplay
                ? Results.Ok(result.Execution)
                : Results.Accepted($"/v1/cases/{caseId}/executions/{result.Execution.ExecutionId}", result.Execution);
        });

        app.MapGet("/v1/cases/{caseId:guid}/executions/{executionId:guid}", async (
            Guid caseId,
            Guid executionId,
            HttpRequest httpRequest,
            GetExecutionHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, caseId, executionId, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapGet("/v1/cases/{caseId:guid}/executions", async (
            Guid caseId,
            HttpRequest httpRequest,
            ListExecutionsHandler handler,
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

        app.MapPost("/v1/cases/{caseId:guid}/reconciliations/{executionId:guid}/resolve", async (
            Guid caseId,
            Guid executionId,
            ResolveReconciliationRequest request,
            HttpRequest httpRequest,
            ResolveReconciliationHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(
                tenantId, caseId, executionId, request, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}
