using KeyValidation.Api.DeveloperIntegration;

namespace KeyValidation.Api;
public sealed class AdminAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (DeveloperMcpConfiguration.IsPublicDeveloperRequest(context))
        {
            await next(context);
            return;
        }

        await context.RequestServices.GetRequiredService<AccountSecurityService>().EnforceAccess(context);
        await context.RequestServices.GetRequiredService<AdminAccessService>().Initialize(context);
        await next(context);
    }
}
