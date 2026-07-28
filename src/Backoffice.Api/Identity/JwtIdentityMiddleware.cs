using Backoffice.Application.Identity;
using Backoffice.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Backoffice.Api.Identity;

/// <summary>
/// Gates every request on a validated bearer JWT when the secure profile is active
/// (`Identity:Mode=jwt`), storing the resolved identity in `HttpContext.Items` for
/// `RequestContext` to prefer over the interim `X-*` headers (spec: identity-security,
/// "Rejection of header-based identity spoofing"). In the default "headers" profile this is
/// a pure pass-through — <see cref="IJwtIdentityValidator"/> is resolved lazily, inside the
/// jwt-mode branch only, so a headers-profile app never needs a configured signing key at all.
/// </summary>
public sealed class JwtIdentityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IOptions<IdentityOptions> identityOptions)
    {
        if (identityOptions.Value.Mode != "jwt")
        {
            await next(context);
            return;
        }

        var authorizationHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorizationHeader) || !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            await WriteUnauthorizedAsync(context, "bearer-token-required");
            return;
        }

        var token = authorizationHeader["Bearer ".Length..].Trim();
        var validator = context.RequestServices.GetRequiredService<IJwtIdentityValidator>();

        ResolvedIdentity identity;
        try
        {
            identity = validator.Validate(token);
        }
        catch (JwtValidationException exception)
        {
            await WriteUnauthorizedAsync(context, exception.Reason);
            return;
        }

        context.Items["ResolvedIdentity"] = identity;
        await next(context);
    }

    private static Task WriteUnauthorizedAsync(HttpContext context, string reason)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return context.Response.WriteAsJsonAsync(new { reason });
    }
}
