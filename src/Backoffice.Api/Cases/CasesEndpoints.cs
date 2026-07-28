using Backoffice.Application.Cases;

namespace Backoffice.Api.Cases;

/// <summary>
/// Maps the case-management surface of contracts/openapi/paths/cases.yaml.
/// </summary>
public static class CasesEndpoints
{
    public static IEndpointRouteBuilder MapCasesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/cases");

        group.MapPost("/", async (
            CreateCaseRequest request,
            HttpRequest httpRequest,
            CreateCaseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);

            var response = await handler.HandleAsync(tenantId, request, actorId, correlationId, cancellationToken);
            return Results.Created($"/v1/cases/{response.CaseId}", response);
        });

        group.MapGet("/", async (
            HttpRequest httpRequest,
            ListCasesHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var response = await handler.HandleAsync(tenantId, cancellationToken);
            return Results.Ok(response);
        });

        group.MapGet("/{caseId:guid}", async (
            Guid caseId,
            HttpRequest httpRequest,
            GetCaseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var response = await handler.HandleAsync(tenantId, caseId, cancellationToken);
            return Results.Ok(response);
        });

        group.MapPost("/{caseId:guid}/cancel", async (
            Guid caseId,
            CancelCaseBody body,
            HttpRequest httpRequest,
            CancelCaseHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var actorId = RequestContext.GetActorId(httpRequest);
            var correlationId = RequestContext.GetOrCreateCorrelationId(httpRequest);
            var expectedVersion = RequestContext.RequireIfMatch(httpRequest);

            var response = await handler.HandleAsync(
                tenantId, caseId, expectedVersion, actorId, correlationId, body.Reason, cancellationToken);
            return Results.Ok(response);
        });

        group.MapGet("/{caseId:guid}/timeline", async (
            Guid caseId,
            HttpRequest httpRequest,
            GetCaseTimelineHandler handler,
            CancellationToken cancellationToken) =>
        {
            var tenantId = RequestContext.RequireTenantId(httpRequest);
            var response = await handler.HandleAsync(tenantId, caseId, cancellationToken);
            return Results.Ok(response);
        });

        return app;
    }
}

public sealed record CancelCaseBody(string Reason);
