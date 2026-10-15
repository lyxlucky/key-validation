using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using KeyValidation.Api;
using KeyValidation.Api.DeveloperIntegration;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 65536);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<DeveloperDocumentationCatalog>();
builder.Services.AddDeveloperMcp(builder.Configuration);
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Database configuration required.")));
builder.Services.AddSingleton<ISecretService, SecretService>();
builder.Services.AddScoped<LicensingService>();
builder.Services.AddScoped<PlanCatalogService>();
builder.Services.AddScoped<PlanAvailability>();
builder.Services.AddScoped<PlanCatalogQueries>();
builder.Services.AddScoped<ClientVersionPolicyService>();
builder.Services.AddSingleton(services => new ClientVersionOptions(
    (services.GetRequiredService<IHostEnvironment>().IsDevelopment() || services.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing")) &&
    services.GetRequiredService<IConfiguration>().GetValue("ClientVersions:AllowLoopbackHttp", false)));
builder.Services.AddSingleton<IOfflineLicenseSigner, OfflineLicenseKeyring>();
builder.Services.AddScoped<OfflineLicenseService>();
builder.Services.AddScoped<AdminAccountService>();
builder.Services.AddScoped<AccountSecurityService>();
builder.Services.AddScoped<ApplicationAccessScope>();
builder.Services.AddScoped<AdminAccessService>();
builder.Services.AddScoped<ICommerceData>(services => services.GetRequiredService<AppDbContext>());
builder.Services.AddScoped<CommerceWorkflow>();
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<OrderFulfillment>();
builder.Services.AddScoped<PortalRequestService>();
builder.Services.AddScoped<ChannelService>();
builder.Services.AddScoped<ApiCredentialService>();
builder.Services.AddScoped<CommerceIdentity>();
builder.Services.AddScoped<CommerceAuthentication>();
builder.Services.AddSingleton<CredentialRateLimiter>();
builder.Services.AddScoped<IOrderDeliveryProtection, OrderDeliveryProtection>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.AddScoped<IDurableEventPublisher, DurableEventPublisher>();
builder.Services.AddScoped<RiskService>();
builder.Services.AddScoped<RecipientAccessService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<OperationsScanner>();
builder.Services.AddScoped<ClientRequestFacts>();
builder.Services.AddSingleton<WebhookTargetPolicy>();
builder.Services.AddSingleton<WebhookTransport>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddScoped<IWebhookDeliveryProcessor, WebhookDeliveryProcessor>();
builder.Services.AddSingleton<BackupSettings>();
builder.Services.AddSingleton<BackupArchive>();
builder.Services.AddSingleton<BackupNativeProcess>();
builder.Services.AddSingleton<BackupDatabaseRestorer>();
builder.Services.AddSingleton<BackupRecoveryService>();
builder.Services.AddScoped<BackupService>();
builder.Services.AddSingleton<RequestMetrics>();
builder.Services.AddSingleton<IValidationRequestMetrics>(services => services.GetRequiredService<RequestMetrics>());
builder.Services.AddSingleton<MonitoringState>();
builder.Services.AddSingleton<IDatabaseAvailabilityProbe, DatabaseAvailabilityProbe>();
builder.Services.AddScoped<MonitoringService>();
builder.Services.AddScoped<ReliabilityRetention>();
builder.Services.AddHostedService<OperationsWorker>();
builder.Services.AddHostedService<BackupWorker>();
builder.Services.AddScoped<ILicensingData>(services => services.GetRequiredService<AppDbContext>());
builder.Services.AddSingleton<ActivationAdmission>();
builder.Services.AddHostedService<RetentionWorker>();
builder.Services.AddOpenApi(o => o.AddDocumentTransformer<SecurityDocumentTransformer>());
builder.Services.AddProblemDetails();
builder.Services.AddIdentity<AdminUser, IdentityRole>(o =>
{
    o.Password.RequiredLength = 12; o.Password.RequireNonAlphanumeric = true; o.Password.RequireDigit = true; o.Password.RequireLowercase = true; o.Password.RequireUppercase = true;
    o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
var localCookies = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "KeyValidation.Admin"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = localCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromMinutes(30); o.SlidingExpiration = true;
    o.Events.OnSigningIn = context => { context.Properties.Items.TryAdd("absolute-issued", context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)); return Task.CompletedTask; };
    o.Events.OnValidatePrincipal = async context =>
    {
        // Public catalog reads never validate business identity or expand content.
        if (DeveloperMcpConfiguration.IsPublicDeveloperRequest(context.HttpContext)) { context.RejectPrincipal(); return; }
        var now = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        if (!context.Properties.Items.TryGetValue("absolute-issued", out var raw) || !long.TryParse(raw, out var issued) || now >= DateTimeOffset.FromUnixTimeSeconds(issued).AddHours(8))
        {
            context.RejectPrincipal(); await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme); return;
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
builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-TOKEN"; o.Cookie.Name = "KeyValidation.Csrf"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = localCookies ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; });
var protection = builder.Services.AddDataProtection().SetApplicationName("KeyValidation");
if (builder.Configuration["DataProtection:Path"] is { Length: > 0 } keyPath) protection.PersistKeysToFileSystem(new DirectoryInfo(keyPath));
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Read", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser())
    .AddPolicy("Operate", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser())
    .AddPolicy("Admin", p => p.AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()
        .RequireAssertion(context => context.Resource is HttpContext http && http.RequestServices.GetRequiredService<ApplicationAccessScope>().Allows(ApplicationPermissions.Administrator)));
foreach (var permission in ApplicationPermissions.All)
    builder.Services.AddAuthorizationBuilder().AddPolicy(permission, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme).RequireAuthenticatedUser()
        .RequireAssertion(context => context.Resource is HttpContext http && http.RequestServices.GetRequiredService<ApplicationAccessScope>().Allows(permission)));
var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
if (builder.Environment.IsDevelopment()) builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear(); o.KnownProxies.Clear();
    foreach (var proxy in builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? []) o.KnownProxies.Add(IPAddress.Parse(proxy));
});
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.OnRejected = (context, _) => { context.HttpContext.Response.Headers.RetryAfter = "60"; return new ValueTask(ApiErrors.Write(context.HttpContext, 429, "RATE_LIMITED")); };
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPerMinute", 600), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = builder.Configuration.GetValue("RateLimiting:LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("heartbeat", context => RateLimitPartition.GetFixedWindowLimiter(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()))), _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
if (args.Length > 0 && args[0] == "backup") { Environment.ExitCode = await BackupCli.Run(app.Services, args); return; }
_ = app.Services.GetRequiredService<ISecretService>();
if (args.Length > 0 && args[0] is "migrate" or "admin") { Environment.ExitCode = await AdminCli.Run(app.Services, args); return; }
app.UseMiddleware<TrustedCountryMiddleware>();
if ((builder.Configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? []).Length > 0) app.UseForwardedHeaders();
app.Use(async (context, next) =>
{
    var start = System.Diagnostics.Stopwatch.GetTimestamp();
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api")) { context.Response.Headers.CacheControl = "no-cache, no-store"; context.Response.Headers.Pragma = "no-cache"; }
    if (!localCookies && !context.Request.IsHttps && (context.Request.Path.StartsWithSegments("/api") || DeveloperMcpConfiguration.IsMcpRequest(context))) { await ApiErrors.Write(context, 400, "HTTPS_REQUIRED"); return; }
    try
    {
        if (context.Request.ContentLength > 65536) throw new RuleException("REQUEST_TOO_LARGE", 413);
        await next();
    }
    catch (ClientVersionException error) { await ApiErrors.Write(context, error.StatusCode, error.Code, version: error.Version); }
    catch (RuleException error) { await ApiErrors.Write(context, error.StatusCode, error.Code, error.BatchId); }
    catch (AntiforgeryValidationException) { await ApiErrors.Write(context, 400, "CSRF_INVALID"); }
    catch (Exception error) when (error is BadHttpRequestException or JsonException or ArgumentException or InvalidDataException) { await ApiErrors.Write(context, error is BadHttpRequestException bad ? bad.StatusCode : 400, "VALIDATION_ERROR"); }
    catch (DbUpdateException error) when (error.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { await ApiErrors.Write(context, 409, "RESOURCE_CONFLICT"); }
    catch (Exception error)
    {
        app.Logger.LogError("Request failed with {ErrorType}; trace {TraceId}", error.GetType().Name, context.TraceIdentifier);
        await ApiErrors.Write(context, 500, "INTERNAL_ERROR");
    }
    finally
    {
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        ApiMetrics.Duration.Record(elapsed, new KeyValuePair<string, object?>("http.response.status_code", context.Response.StatusCode));
        context.RequestServices.GetRequiredService<IValidationRequestMetrics>().Record((context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText, context.Response.StatusCode, elapsed);
    }
});
app.UseStatusCodePages(context => ApiErrors.Write(context.HttpContext, context.HttpContext.Response.StatusCode, context.HttpContext.Response.StatusCode switch { 400 => "VALIDATION_ERROR", 401 => "UNAUTHENTICATED", 403 => "FORBIDDEN", 404 => "NOT_FOUND", _ => "REQUEST_FAILED" }));
if (builder.Environment.IsDevelopment()) app.UseCors();
app.Use(DeveloperMcpConfiguration.GuardRequest);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    if (DeveloperMcpConfiguration.IsPublicDeveloperRequest(context)) { await next(); return; }
    await context.RequestServices.GetRequiredService<AccountSecurityService>().EnforceAccess(context);
    await context.RequestServices.GetRequiredService<AdminAccessService>().Initialize(context);
    await next();
});
app.UseAuthorization();
// Admission happens before CSRF/body parsing; endpoint filtering adds the
// application-specific bucket after the framework has bound the request once.
app.UseRateLimiter();
app.UseMiddleware<CommerceBearerMiddleware>();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/admin") && context.Request.Method is not "GET" and not "HEAD" and not "OPTIONS") await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
    await next();
});
app.MapOpenApi();
app.MapGet("/health/live", () => TypedResults.Ok(new { status = "healthy" })).ExcludeFromDescription();
app.MapGet("/health/ready", async (AppDbContext db) => { try { await db.Database.ExecuteSqlRawAsync("SELECT 1"); return Results.Ok(new { status = "ready" }); } catch { return Results.Problem(statusCode: 503, title: "DATABASE_UNAVAILABLE"); } }).ExcludeFromDescription();
app.MapApi();
app.MapDeveloperDocumentationApi();
app.MapDeveloperMcp();
app.MapPlanApi();
app.MapManagementApi();
app.MapAccountApi();
app.MapRebindingApi();
app.MapAccountSecurityApi();
app.MapPolicyApi();
app.MapClientExtensionsApi();
app.MapAccessApi();
app.MapRoleApi();
app.MapReliabilityApi();
app.MapWebhookApi();
app.MapBackupApi();
app.MapMonitoringApi();
app.MapCommerceApi();
app.MapPortalApi();
app.MapExternalApi();
app.Run();
public partial class Program;


