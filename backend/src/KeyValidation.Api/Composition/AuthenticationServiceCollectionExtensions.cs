using System.Globalization;
using KeyValidation.Api.DeveloperIntegration;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;

namespace KeyValidation.Api;
public static class AuthenticationServiceCollectionExtensions
{
    internal static IServiceCollection AddAccountSecurity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddIdentity<AdminUser, IdentityRole>(o =>
        {
            o.Password.RequiredLength = 12;
            o.Password.RequireNonAlphanumeric = true;
            o.Password.RequireDigit = true;
            o.Password.RequireLowercase = true;
            o.Password.RequireUppercase = true;
            o.Lockout.MaxFailedAccessAttempts = 5;
            o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
        services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
        var localCookies = environment.IsDevelopment() || environment.IsEnvironment("Testing");
        services.ConfigureApplicationCookie(o =>
        {
            o.Cookie.Name = "KeyValidation.Admin";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = localCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            o.SlidingExpiration = true;
            o.Events.OnSigningIn = context =>
            {
                context.Properties.Items.TryAdd("absolute-issued", context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
                return Task.CompletedTask;
            };
            o.Events.OnValidatePrincipal = async context =>
            {
                // Public catalog reads never validate business identity or expand content.
                if (DeveloperMcpConfiguration.IsPublicDeveloperRequest(context.HttpContext))
                {
                    context.RejectPrincipal();
                    return;
                }

                var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
                if (!context.Properties.Items.TryGetValue("absolute-issued", out var raw) || !long.TryParse(raw, out var issued) || now >= DateTimeOffset.FromUnixTimeSeconds(issued).AddHours(8))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    return;
                }

                await SecurityStampValidator.ValidatePrincipalAsync(context);
                if (context.Principal?.Identity?.IsAuthenticated == true)
                {
                    var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<AdminUser>>();
                    var user = await users.GetUserAsync(context.Principal);
                    if (user is null || user.DisabledAt is not null)
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                    }
                }
            };
            o.Events.OnRedirectToLogin = context => ApiErrors.Write(context.HttpContext, 401, "UNAUTHENTICATED");
            o.Events.OnRedirectToAccessDenied = context => ApiErrors.Write(context.HttpContext, 403, "FORBIDDEN");
        });
        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-CSRF-TOKEN";
            o.Cookie.Name = "KeyValidation.Csrf";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = localCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        var protection = services.AddDataProtection().SetApplicationName("KeyValidation");
        if (configuration["DataProtection:Path"] is { Length: > 0 } keyPath)
            protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
        services.AddAuthorizationBuilder().AddPolicy("Read", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()).AddPolicy("Operate", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()).AddPolicy("Admin", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser().RequireAssertion(context => context.Resource is HttpContext http && http.RequestServices.GetRequiredService<ApplicationAccessScope>().Allows(ApplicationPermissions.Administrator)));
        foreach (var permission in ApplicationPermissions.All)
            services.AddAuthorizationBuilder().AddPolicy(permission, policy => policy.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser().RequireAssertion(context => context.Resource is HttpContext http && http.RequestServices.GetRequiredService<ApplicationAccessScope>().Allows(permission)));
        return services;
    }
}
