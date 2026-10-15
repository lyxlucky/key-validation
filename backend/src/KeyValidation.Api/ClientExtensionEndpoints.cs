using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

public static class ClientExtensionEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    public static void MapClientExtensionsApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Client extensions").RequireAuthorization("Read");
        // The service permits either stored platform.manage or licenses.read.
        admin.MapGet("/applications/{id:guid}/version-policy", (Guid id, ClientVersionPolicyService service) => service.Get(id));
        admin.MapPut("/applications/{id:guid}/version-policy", (Guid id, UpdateVersionPolicy request, HttpContext context, ClientVersionPolicyService service) => service.Update(id, request, ActorOf(context))).RequireAuthorization("Admin");
        admin.MapGet("/offline-licenses/configuration", (OfflineLicenseService service) => service.Configuration()).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapGet("/offline-licenses", (OfflineLicenseService service, Guid? applicationId = null, Guid? licenseId = null, int page = 1, int pageSize = 20) => service.List(applicationId, licenseId, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/offline-licenses", async (SignOfflineLicense request, HttpContext context, OfflineLicenseService service) =>
        {
            var result = await service.Sign(request, ActorOf(context)); return TypedResults.Created($"/api/admin/offline-licenses?licenseId={result.Record.LicenseId}", result);
        }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapPost("/offline-licenses/{id:guid}/revoke", async (Guid id, HttpContext context, OfflineLicenseService service) =>
        { await service.Revoke(id, ActorOf(context)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        app.MapGet("/api/client/applications/{appCode}/version", (string appCode, ClientVersionPolicyService service) => service.Public(appCode)).WithTags("Software clients");
        app.MapPost("/api/client/offline-licenses/verify", (VerifyOfflineLicense request, OfflineLicenseService service) => service.Verify(request)).WithTags("Software clients");
    }
}
