using Microsoft.AspNetCore.Antiforgery;

namespace KeyValidation.Api;
public sealed class AdminAntiforgeryMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/api/admin") && context.Request.Method is not "GET" and not "HEAD" and not "OPTIONS")
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        await next(context);
    }
}
