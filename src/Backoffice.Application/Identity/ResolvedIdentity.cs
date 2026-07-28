namespace Backoffice.Application.Identity;

/// <summary>
/// The caller identity derived from either a validated JWT (secure/jwt profile) or the
/// interim `X-*` headers (default/headers profile) — a single shape either source resolves
/// to, so downstream code (RequestContext, endpoints) doesn't need to know which one was
/// used (spec: identity-security).
/// </summary>
public sealed record ResolvedIdentity(
    string ActorId,
    string SubjectType,
    IReadOnlyList<string> Roles,
    string TenantId,
    string AuthenticationMethod,
    string TokenId);
