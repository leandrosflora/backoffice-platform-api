using System.Diagnostics;
using Backoffice.Application.Approvals;
using Backoffice.Application.Cases;
using Backoffice.Application.Documents;
using Backoffice.Application.Executions;
using Backoffice.Application.Policy;
using Backoffice.Application.Recommendations;
using Backoffice.Domain.Cases;
using Backoffice.Domain.Documents;
using Backoffice.Domain.Eventing;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Backoffice.Api;

/// <summary>
/// Maps domain/application exceptions to RFC7807 ProblemDetails, per
/// contracts/openapi/platform-api.yaml (every error body carries a traceId).
/// </summary>
public sealed class BackofficeExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            CaseNotFoundException => (StatusCodes.Status404NotFound, "Case not found"),
            DocumentNotFoundException => (StatusCodes.Status404NotFound, "Document not found"),
            InvestigationNotFoundException => (StatusCodes.Status404NotFound, "Investigation not found"),
            ExecutionNotFoundException => (StatusCodes.Status404NotFound, "Execution not found"),
            DeadLetterNotFoundException => (StatusCodes.Status404NotFound, "Dead letter not found"),
            PolicyDeniedException => (StatusCodes.Status403Forbidden, "Policy denied"),
            ObligationNotSatisfiedException => (StatusCodes.Status403Forbidden, "Obligation not satisfied"),
            PolicyDecisionPointUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Policy decision point unavailable"),
            CaseVersionConflictException => (StatusCodes.Status409Conflict, "Case version conflict"),
            InvalidCaseTransitionException => (StatusCodes.Status409Conflict, "Invalid case transition"),
            InvalidDocumentTransitionException => (StatusCodes.Status409Conflict, "Invalid document transition"),
            StaleRecommendationException => (StatusCodes.Status409Conflict, "Stale recommendation version"),
            CaseNotAwaitingApprovalException => (StatusCodes.Status409Conflict, "Case not awaiting approval"),
            NoValidApprovalException => (StatusCodes.Status409Conflict, "No valid approval"),
            IdempotencyConflictException => (StatusCodes.Status409Conflict, "Idempotency key conflict"),
            ExecutionNotAwaitingReconciliationException => (StatusCodes.Status409Conflict, "Execution not awaiting reconciliation"),
            DeadLetterAlreadyReplayedException => (StatusCodes.Status409Conflict, "Dead letter already replayed"),
            NoGroundingEvidenceException => (StatusCodes.Status422UnprocessableEntity, "No grounding evidence"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Bad request"),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
        };

        var problemDetails = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = exception.Message,
            Extensions =
            {
                ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier,
            },
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
