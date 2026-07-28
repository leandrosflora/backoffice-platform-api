namespace Backoffice.Application.Policy;

/// <summary>
/// Single choke point every gated action calls through: evaluates via OPA, fails closed on
/// PDP unavailability, denies on allow=false, and — for the subset of obligations that
/// correspond to a concrete server-side check — verifies each one before letting the action
/// proceed (spec: policy-authorization). Obligations without a supplied check (e.g.
/// audit-decision, tenant-match, redact-sensitive-data) are advisory in this baseline: no
/// audit/redaction pipeline exists yet (sections 9/11), so they do not block.
/// </summary>
public sealed class PolicyEnforcer(IPolicyDecisionClient client)
{
    private static readonly HashSet<string> VerifiableObligations =
    [
        "verify-case-version",
        "verify-segregation-of-duties",
        "verify-approval",
        "verify-idempotency",
        "verify-evidence",
    ];

    public async Task<AuthorizationDecision> EnforceAsync(
        AuthorizationInput input,
        IReadOnlyDictionary<string, bool>? obligationResults = null,
        CancellationToken cancellationToken = default)
    {
        var decision = await client.EvaluateAsync(input, cancellationToken);

        if (!decision.PdpAvailable)
        {
            throw new PolicyDecisionPointUnavailableException();
        }

        if (!decision.Allow)
        {
            throw new PolicyDeniedException(decision.Reason);
        }

        foreach (var obligation in decision.Obligations.Where(VerifiableObligations.Contains))
        {
            if (obligationResults is null || !obligationResults.TryGetValue(obligation, out var satisfied) || !satisfied)
            {
                throw new ObligationNotSatisfiedException(obligation);
            }
        }

        return decision;
    }
}
