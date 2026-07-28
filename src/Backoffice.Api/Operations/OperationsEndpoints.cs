using Backoffice.Application.Eventing;

namespace Backoffice.Api.Operations;

/// <summary>
/// Maps contracts/openapi/eventing-operations-api.yaml — the operator-facing surface for
/// timer scheduling and outbox/dead-letter inspection and replay.
/// </summary>
public static class OperationsEndpoints
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/operations/cases/{caseId:guid}/timers", async (
            Guid caseId,
            ScheduleTimerRequest request,
            HttpRequest httpRequest,
            ScheduleTimerHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, caseId, request, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapGet("/v1/operations/outbox", async (
            HttpRequest httpRequest,
            ListOutboxHandler handler,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, limit ?? 100, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapGet("/v1/operations/dead-letters", async (
            HttpRequest httpRequest,
            ListDeadLettersHandler handler,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, limit ?? 100, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapGet("/v1/operations/timers", async (
            HttpRequest httpRequest,
            ListTimersHandler handler,
            int? limit,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, limit ?? 100, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        app.MapPost("/v1/operations/dead-letters/{deadLetterId:long}/replay", async (
            long deadLetterId,
            ReplayDeadLetterRequest request,
            HttpRequest httpRequest,
            ReplayDeadLetterHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var roles = RequestContext.GetRoles(httpRequest);
            var subjectType = RequestContext.GetSubjectType(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, deadLetterId, request, actorId, roles, subjectType, correlationId, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}
