namespace Backoffice.Application.Policy;

/// <summary>
/// Calls the OPA Policy Decision Point unmodified (policies/authorization.rego). Never
/// throws — any connectivity failure, timeout, or non-2xx response resolves to
/// <see cref="AuthorizationDecision.Unavailable"/> so the caller always fails closed
/// (spec: policy-authorization, "Fail-closed on PDP unavailability").
/// </summary>
public interface IPolicyDecisionClient
{
    Task<AuthorizationDecision> EvaluateAsync(AuthorizationInput input, CancellationToken cancellationToken = default);
}
