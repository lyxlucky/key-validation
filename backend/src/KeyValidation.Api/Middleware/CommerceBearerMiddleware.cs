using System.Security.Claims;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;
public sealed class CommerceBearerMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CommerceAuthentication authentication, CommerceIdentity identity)
    {
        var portal = context.Request.Path.StartsWithSegments("/api/portal");
        var api = context.Request.Path.StartsWithSegments("/api/v1");
        if (portal || api)
        {
            // These route families never inherit an admin cookie principal.
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            if (!(portal && context.Request.Method == "POST" && context.Request.Path.Equals("/api/portal/sessions", StringComparison.OrdinalIgnoreCase)))
            {
                var value = context.Request.Headers.Authorization.ToString();
                if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    throw new RuleException("UNAUTHENTICATED", 401);
                try
                {
                    if (api)
                        await authentication.AuthenticateApi(value[7..]);
                    else
                        await authentication.AuthenticatePortal(value[7..]);
                }
                catch (RuleException error)when (error.StatusCode == 429)
                {
                    context.Response.Headers.RetryAfter = "60";
                    throw;
                }

                context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, identity.ActorId)], api ? "api-credential" : "portal-session"));
            }
        }

        await next(context);
    }
}
