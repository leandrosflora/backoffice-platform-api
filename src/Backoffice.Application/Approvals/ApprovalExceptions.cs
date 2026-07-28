namespace Backoffice.Application.Approvals;

/// <summary>The decision targets a recommendation version that is no longer current. Maps to 409.</summary>
public sealed class StaleRecommendationException(long expectedVersion, long actualVersion)
    : Exception($"Recommendation version {expectedVersion} is stale; current version is {actualVersion}.")
{
}

/// <summary>The case is no longer awaiting approval (e.g. it just expired). Maps to 409.</summary>
public sealed class CaseNotAwaitingApprovalException(Guid caseId)
    : Exception($"Case '{caseId}' is not currently awaiting approval.")
{
}
