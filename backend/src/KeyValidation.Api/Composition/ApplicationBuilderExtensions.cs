using KeyValidation.Api.DeveloperIntegration;

namespace KeyValidation.Api;
public static class ApplicationBuilderExtensions
{
    public static WebApplication UseKeyValidation(this WebApplication app)
    {
        // Country trust must see the direct peer before forwarded headers are applied.
        app.UseMiddleware<TrustedCountryMiddleware>();
        if ((app.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? []).Length > 0)
            app.UseForwardedHeaders();
        var localCookies = app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing");
        app.UseMiddleware<ApiRequestMiddleware>(localCookies, app.Logger);
        app.UseStatusCodePages(context => ApiErrors.Write(context.HttpContext, context.HttpContext.Response.StatusCode, context.HttpContext.Response.StatusCode switch
        {
            400 => "VALIDATION_ERROR",
            401 => "UNAUTHENTICATED",
            403 => "FORBIDDEN",
            404 => "NOT_FOUND",
            _ => "REQUEST_FAILED"
        }));
        if (app.Environment.IsDevelopment())
            app.UseCors();
        app.Use(DeveloperMcpConfiguration.GuardRequest);
        app.UseAuthentication();
        app.UseMiddleware<AdminAccessMiddleware>();
        app.UseAuthorization();
        // Admission precedes CSRF and body parsing; the endpoint filter adds the app bucket.
        app.UseRateLimiter();
        app.UseMiddleware<CommerceBearerMiddleware>();
        app.UseMiddleware<AdminAntiforgeryMiddleware>();
        return app;
    }
}
