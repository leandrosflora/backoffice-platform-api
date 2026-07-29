using Backoffice.Application.Identity;
using Backoffice.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Backoffice.Api.Identity;

/// <summary>Reads the current request's <see cref="ResolvedIdentity"/> (set by
/// <see cref="JwtIdentityMiddleware"/> only in the "jwt" profile) via
/// <see cref="IHttpContextAccessor"/>, so <c>PolicyEnforcer</c> can enrich every
/// authorization call without any command handler needing to know about HTTP at all.</summary>
public sealed class HttpContextCallerIdentityAccessor(
    IHttpContextAccessor httpContextAccessor, IOptions<IdentityOptions> identityOptions) : ICallerIdentityAccessor
{
    private ResolvedIdentity? Current =>
        httpContextAccessor.HttpContext?.Items.TryGetValue("ResolvedIdentity", out var value) == true
            ? value as ResolvedIdentity
            : null;

    public string? AuthenticationMethod => Current?.AuthenticationMethod;
    public string? TokenId => Current?.TokenId;
    public string? Purpose => Current?.Purpose;
    public string IdentityMode => identityOptions.Value.Mode;
}
