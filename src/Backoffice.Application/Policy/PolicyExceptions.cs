namespace Backoffice.Application.Policy;

/// <summary>OPA evaluated the request and denied it. Maps to 403.</summary>
public sealed class PolicyDeniedException(string reason) : Exception($"Policy denied the action: {reason}.")
{
}

/// <summary>OPA could not be reached or evaluated. Fails closed — maps to 503, not open access.</summary>
public sealed class PolicyDecisionPointUnavailableException()
    : Exception("The policy decision point is unavailable; the action was denied (fail-closed).")
{
}

/// <summary>
/// OPA allowed the action but returned an obligation this server-side check could not
/// confirm (spec: policy-authorization, "Obligation enforcement after allow"). Maps to 403.
/// </summary>
public sealed class ObligationNotSatisfiedException(string obligation)
    : Exception($"Obligation '{obligation}' was not satisfied.")
{
}
