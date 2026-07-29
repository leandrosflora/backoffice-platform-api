namespace Backoffice.Application.Identity;

/// <summary>Thrown for any bearer-token validation failure — signature, issuer/audience,
/// TTL, missing claim, or invalid subject_type/purpose. Always maps to 401 (spec:
/// identity-security — none of these distinguish 400 vs 401; an unauthenticated caller
/// never learns more than "unauthorized").</summary>
public sealed class JwtValidationException(string reason, Exception? inner = null)
    : Exception($"JWT validation failed: {reason}.", inner)
{
    public string Reason { get; } = reason;
}

public interface IJwtIdentityValidator
{
    /// <summary>Validates a raw bearer token (without the "Bearer " prefix) and returns the
    /// resolved identity, or throws <see cref="JwtValidationException"/>.</summary>
    Task<ResolvedIdentity> ValidateAsync(string bearerToken, CancellationToken cancellationToken = default);
}
