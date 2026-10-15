using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;

namespace KeyValidation.Api;

public static class Endpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    private static string Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || value.Length is < 40 or > 256) throw new RuleException("SESSION_INVALID", 401);
        return value[7..];
    }
    public static void MapApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Administration").RequireAuthorization("Read");
        admin.MapGet("/dashboard", (LicensingService service, Guid? applicationId = null, Guid? channelId = null) => service.Dashboard(applicationId, channelId)).RequireAuthorization(ApplicationPermissions.ReadReports);
        admin.MapGet("/applications", (LicensingService service, int page = 1, int pageSize = 20) => service.Applications(page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/applications", async (CreateApplication request, HttpContext c, LicensingService service) => { var result = await service.CreateApplication(request, ActorOf(c)); return TypedResults.Created($"/api/admin/applications/{result.Id}", result); }).RequireAuthorization("Admin");
        admin.MapPost("/applications/{id:guid}/disable", async (Guid id, HttpContext c, LicensingService s) => { await s.SetApplication(id, true, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Admin");
        admin.MapPost("/applications/{id:guid}/enable", async (Guid id, HttpContext c, LicensingService s) => { await s.SetApplication(id, false, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Admin");
        admin.MapPost("/license-batches", async (IssueBatch request, HttpContext c, LicensingService s) => { var result = await s.Issue(request, ActorOf(c)); return TypedResults.Created($"/api/admin/license-batches?requestId={result.Batch.RequestId}", result); }).RequireAuthorization("Operate", ApplicationPermissions.IssueLicenses);
        admin.MapGet("/license-batches", (HttpContext c, LicensingService s, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? requestId = null, Guid? channelId = null) => s.Batches(page, pageSize, applicationId, requestId, ActorOf(c), channelId)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/license-batches/{id:guid}/disable", async (Guid id, HttpContext c, LicensingService s) => { await s.DisableBatch(id, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapGet("/licenses", (LicensingService s, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? batchId = null, string? status = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? note = null, string? tag = null, Guid? channelId = null) => s.Licenses(page, pageSize, applicationId, batchId, status, from, to, note, tag, channelId)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/licenses/search", (SearchLicense request, LicensingService s) => s.Search(request.LicenseKey)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapGet("/licenses/{id:guid}", (Guid id, LicensingService s) => s.Detail(id)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/licenses/{id:guid}/disable", async (Guid id, HttpContext c, LicensingService s) => { await s.SetLicense(id, true, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapPost("/licenses/{id:guid}/enable", async (Guid id, HttpContext c, LicensingService s) => { await s.SetLicense(id, false, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapPost("/device-bindings/{id:guid}/release", async (Guid id, HttpContext c, LicensingService s) => { await s.ReleaseBinding(id, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapPost("/sessions/{id:guid}/revoke", async (Guid id, HttpContext c, LicensingService s) => { await s.RevokeSession(id, ActorOf(c)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapGet("/audit-logs", (AuditLogService s, int page = 1, int pageSize = 20, string? actorId = null, Guid? targetId = null, string? action = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? category = null, string? actorName = null) => s.List(page, pageSize, actorId, targetId, action, from, to, category, actorName)).RequireAuthorization(ApplicationPermissions.ReadAudit);
        admin.MapGet("/login-logs", (AuditLogService s, int page = 1, int pageSize = 20, string? actorId = null, Guid? targetId = null, string? action = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? actorName = null) => s.List(page, pageSize, actorId, targetId, action, from, to, "login", actorName)).RequireAuthorization(ApplicationPermissions.ReadAudit);
        var client = app.MapGroup("/api/client").WithTags("Software clients");
        client.MapPost("/licenses/activate", async (ActivateLicense request, LicensingService s) => { var result = await s.Activate(request); ApiMetrics.Activations.Add(1, new KeyValuePair<string, object?>("outcome", "success")); return result; }).AddEndpointFilter<ActivationAdmissionFilter>();
        client.MapPost("/sessions/heartbeat", (HttpContext c, LicensingService s) => s.Heartbeat(Token(c), c.Request.Headers.TryGetValue("X-Client-Version", out var version) ? version.ToString() : null)).RequireRateLimiting("heartbeat");
        client.MapPost("/sessions/logout", async (HttpContext c, LicensingService s) => { await s.LogoutClient(Token(c)); return TypedResults.NoContent(); }).RequireRateLimiting("heartbeat");
    }
}
