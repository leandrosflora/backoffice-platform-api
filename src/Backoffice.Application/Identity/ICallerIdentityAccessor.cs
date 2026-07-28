namespace Backoffice.Application.Identity;

/// <summary>
/// Exposes the current request's `authentication_method`/`token_id` (from the resolved JWT,
/// when the secure profile is active) to <c>PolicyEnforcer</c>, which enriches every
/// <c>AuthorizationInput.Subject</c> with them before evaluating — so no individual command
/// handler needs to thread these two fields through its own signature just to satisfy the
/// PDP contract (spec: identity-security, "pass authentication_method/token_id through to
/// the PDP"). Returns nulls in the default header-based profile or outside an HTTP request
/// (e.g. a background worker), which is exactly the "not a validated JWT" signal
/// `policies/authorization.rego`'s `identity_profile_valid` rule keys off.
/// </summary>
public interface ICallerIdentityAccessor
{
    string? AuthenticationMethod { get; }
    string? TokenId { get; }

    /// <summary>The server-wide identity profile ("headers" or "jwt"), passed to the PDP as
    /// `context.identity_mode` only when it's "jwt" (spec: identity-security, "Rejection of
    /// header-based identity spoofing") — never set at all in the default "headers" profile,
    /// since `policies/authorization.rego`'s `identity_profile_valid` rule treats an absent
    /// `identity_mode` key differently from an explicit non-"jwt" value.</summary>
    string IdentityMode { get; }
}

/// <summary>Default registration so <c>PolicyEnforcer</c> can always be resolved (e.g. in
/// Backoffice.Workers, which has no HttpContext and never actually calls EnforceAsync).</summary>
public sealed class NullCallerIdentityAccessor : ICallerIdentityAccessor
{
    public string? AuthenticationMethod => null;
    public string? TokenId => null;
    public string IdentityMode => "headers";
}
