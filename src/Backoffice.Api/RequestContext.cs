using System.Globalization;

namespace Backoffice.Api;

/// <summary>
/// Extracts the tenant/actor/correlation context from request headers.
/// This is an interim, header-based extraction used before section 11
/// (identity-security) replaces it with values derived from a validated
/// EdDSA JWT — see specs/identity-security/spec.md.
/// </summary>
public static class RequestContext
{
    public const string TenantHeader = "X-Tenant-Id";
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string SubjectHeader = "X-Subject-Id";

    public static string RequireTenantId(HttpRequest request)
    {
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

    public static string GetActorId(HttpRequest request) =>
        request.Headers.TryGetValue(SubjectHeader, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : "unknown-actor";

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
    /// Interim stand-in for the approver's authority limit (alçada), which section 11
    /// (identity-security) will instead derive from a validated JWT claim. Defaults to
    /// unlimited when absent, since there is no real identity/role system yet — tests and
    /// callers that need the alçada check to actually bind must set this header explicitly.
    /// </summary>
    public const string AuthorityLimitHeader = "X-Authority-Limit";

    public static decimal GetAuthorityLimit(HttpRequest request) =>
        request.Headers.TryGetValue(AuthorityLimitHeader, out var value)
        && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : decimal.MaxValue;
}
