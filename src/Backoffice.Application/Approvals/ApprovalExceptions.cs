namespace Backoffice.Application.Approvals;

/// <summary>Recommender cannot approve their own recommendation. Maps to 403.</summary>
public sealed class SelfApprovalException(string actorId)
    : Exception($"Actor '{actorId}' cannot decide on a recommendation they authored.")
{
}

/// <summary>Approver's authority limit is below the case's disputed amount. Maps to 403.</summary>
public sealed class AuthorityLimitExceededException(decimal authorityLimit, decimal disputedAmount)
    : Exception($"Authority limit {authorityLimit} is below the disputed amount {disputedAmount}.")
{
}

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
