using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

public static class RebindingEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);

    public static void MapRebindingApi(this WebApplication app)
    {
        var policies = app.MapGroup("/api/admin/licenses/{id:guid}/device-policy").WithTags("Device rebinding").RequireAuthorization("Read");
        policies.MapGet("", (Guid id, LicensingService service) => service.DevicePolicy(id)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        policies.MapPatch("", (Guid id, UpdateDevicePolicy request, HttpContext context, LicensingService service) =>
            service.UpdateDevicePolicy(id, request, ActorOf(context))).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        policies.MapPost("/reset", (Guid id, ResetDevicePolicy request, HttpContext context, LicensingService service) =>
            service.ResetDevicePolicy(id, request, ActorOf(context))).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        app.MapPost("/api/client/device-bindings/release", async (HttpContext context, LicensingService service) =>
        {
            var value = context.Request.Headers.Authorization.ToString();
            if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || value.Length is < 40 or > 256)
                throw new RuleException("SESSION_INVALID", 401);
            await service.ReleaseCurrentBinding(value[7..]);
            return TypedResults.NoContent();
        }).WithTags("Software clients").RequireRateLimiting("heartbeat");
    }
}
