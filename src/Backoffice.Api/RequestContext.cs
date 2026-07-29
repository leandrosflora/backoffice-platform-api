using System.Globalization;
using Backoffice.Application.Identity;

namespace Backoffice.Api;

/// <summary>
/// Extracts the tenant/actor/correlation context from the request. In the default "headers"
/// profile (sections 1–10), this reads the interim `X-*` headers directly, same as always.
/// In the secure "jwt" profile, `JwtIdentityMiddleware` has already validated the bearer
/// token and stored a <see cref="ResolvedIdentity"/> in `HttpContext.Items` — every method
/// below prefers that over headers when present, which is how spoofed `X-Subject-*`/
/// `X-Roles`/`X-Tenant-Id` headers are ignored in the secure profile (spec:
/// identity-security, "Rejection of header-based identity spoofing") without any change to
/// the ~20 endpoint call sites that already call these methods.
/// </summary>
public static class RequestContext
{
    private const string ResolvedIdentityKey = "ResolvedIdentity";

    public const string TenantHeader = "X-Tenant-Id";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string SubjectHeader = "X-Subject-Id";

    private static ResolvedIdentity? TryGetResolvedIdentity(HttpRequest request) =>
        request.HttpContext.Items.TryGetValue(ResolvedIdentityKey, out var value) ? value as ResolvedIdentity : null;

    public static string RequireTenantId(HttpRequest request)
    {
        if (TryGetResolvedIdentity(request) is { } identity)
        {
            return identity.TenantId;
        }

        if (!request.Headers.TryGetValue(TenantHeader, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new BadHttpRequestException($"Header '{TenantHeader}' is required.");
        }

        return value.ToString();
    }

    public static Guid GetOrCreateCorrelationId(HttpRequest request) =>
        request.Headers.TryGetValue(CorrelationHeader, out var value) && Guid.TryParse(value, out var parsed)
            ? parsed
            : Guid.NewGuid();

    public static string GetActorId(HttpRequest request)
    {
        if (TryGetResolvedIdentity(request) is { } identity)
        {
            return identity.ActorId;
        }

        return request.Headers.TryGetValue(SubjectHeader, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : "unknown-actor";
    }

    /// <summary>The expected case version for optimistic concurrency (spec: case-management).</summary>
    public static long RequireIfMatch(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("If-Match", out var value) || !long.TryParse(value, out var version))
        {
            throw new BadHttpRequestException("Header 'If-Match' with the expected case version is required.");
        }

        return version;
    }

    /// <summary>
    /// In the JWT profile the alçada comes only from the signed authority_limit claim.
    /// A missing claim resolves to zero (fail closed), and a spoofed header has no effect.
    /// The interim header remains available only in the local headers profile.
    /// </summary>
    public const string AuthorityLimitHeader = "X-Authority-Limit";

    public static decimal GetAuthorityLimit(HttpRequest request)
    {
        if (TryGetResolvedIdentity(request) is { } identity)
        {
            return identity.AuthorityLimit ?? 0m;
        }

        return request.Headers.TryGetValue(AuthorityLimitHeader, out var value)
            && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : decimal.MaxValue;
    }

    /// <summary>Required for every governed-execution request (spec: governed-execution, BR-017).</summary>
    public static string RequireIdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Idempotency-Key", out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new BadHttpRequestException("Header 'Idempotency-Key' is required.");
        }

        return value.ToString();
    }

    /// <summary>
    /// Interim stand-in for the roles a validated JWT would carry (section 11 replaces this).
    /// Comma-separated; absent/empty means no roles, so OPA's default-deny applies rather
    /// than silently granting broad access.
    /// </summary>
    public const string RolesHeader = "X-Roles";

    public static IReadOnlyList<string> GetRoles(HttpRequest request)
    {
        if (TryGetResolvedIdentity(request) is { } identity)
        {
            return identity.Roles;
        }

        return request.Headers.TryGetValue(RolesHeader, out var value)
            ? value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }

    /// <summary>Interim stand-in for the subject_type a validated JWT would carry (HUMAN|WORKLOAD).</summary>
    public const string SubjectTypeHeader = "X-Subject-Type";

    public static string GetSubjectType(HttpRequest request)
    {
        if (TryGetResolvedIdentity(request) is { } identity)
        {
            return identity.SubjectType;
        }

        return request.Headers.TryGetValue(SubjectTypeHeader, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : Backoffice.Application.Policy.PolicySubjectTypes.Human;
    }
}
