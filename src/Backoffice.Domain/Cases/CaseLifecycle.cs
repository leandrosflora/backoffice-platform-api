namespace Backoffice.Domain.Cases;

/// <summary>
/// Authoritative allowed-transition table for the canonical case lifecycle
/// (docs/functional/case-lifecycle.md). Any transition not listed here is invalid.
/// </summary>
public static class CaseLifecycle
{
    private static readonly Dictionary<CaseState, CaseState[]> AllowedTransitions = new()
    {
        [CaseState.Created] = [CaseState.AwaitingDocuments, CaseState.DocumentsReceived, CaseState.Cancelled],
        [CaseState.AwaitingDocuments] = [CaseState.DocumentsReceived, CaseState.Cancelled, CaseState.Expired],
        [CaseState.DocumentsReceived] = [CaseState.DocumentsValidated, CaseState.AwaitingDocuments, CaseState.Cancelled, CaseState.Expired],
        [CaseState.DocumentsValidated] = [CaseState.UnderInvestigation, CaseState.Cancelled],
        [CaseState.UnderInvestigation] = [CaseState.DecisionProposed, CaseState.Failed, CaseState.Cancelled],
        [CaseState.DecisionProposed] = [CaseState.AwaitingApproval, CaseState.Cancelled],
        [CaseState.AwaitingApproval] = [CaseState.Approved, CaseState.Rejected, CaseState.MoreEvidenceRequired, CaseState.Expired, CaseState.Cancelled],
        [CaseState.MoreEvidenceRequired] = [CaseState.UnderInvestigation, CaseState.DocumentsReceived, CaseState.Cancelled],
        [CaseState.Approved] = [CaseState.ExecutionPending, CaseState.Cancelled],
        [CaseState.ExecutionPending] = [CaseState.Executed, CaseState.ReconciliationRequired, CaseState.Failed],
        [CaseState.ReconciliationRequired] = [CaseState.Executed, CaseState.Failed],
        [CaseState.Executed] = [CaseState.Closed],
        [CaseState.Rejected] = [CaseState.Closed],
        [CaseState.Failed] = [CaseState.Closed],
        [CaseState.Closed] = [],
        [CaseState.Cancelled] = [],
        [CaseState.Expired] = [CaseState.Closed],
    };

    public static bool CanTransition(CaseState from, CaseState to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static bool IsTerminal(CaseState state) =>
        AllowedTransitions.TryGetValue(state, out var allowed) && allowed.Length == 0;
}
