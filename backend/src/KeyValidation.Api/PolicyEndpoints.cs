using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

public static class PolicyEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);

    public static void MapPolicyApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Authorization policies").RequireAuthorization("Read");
        admin.MapPost("/licenses/{id:guid}/freeze", async (Guid id, FreezeLicense request, HttpContext context, LicensingService service) =>
        { await service.FreezeLicense(id, request, true, ActorOf(context)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapPost("/licenses/{id:guid}/unfreeze", async (Guid id, FreezeLicense request, HttpContext context, LicensingService service) =>
        { await service.FreezeLicense(id, request, false, ActorOf(context)); return TypedResults.NoContent(); }).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapGet("/licenses/{id:guid}/freeze-history", (Guid id, LicensingService service, int page = 1, int pageSize = 20) => service.FreezeHistory(id, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapGet("/applications/{id:guid}/trial-policy", (Guid id, LicensingService service) => service.TrialPolicy(id)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPut("/applications/{id:guid}/trial-policy", (Guid id, UpdateTrialPolicy request, HttpContext context, LicensingService service) => service.UpdateTrialPolicy(id, request, ActorOf(context))).RequireAuthorization("Admin");
        app.MapPost("/api/client/trials/activate", async (ActivateTrial request, LicensingService service) =>
        {
            var result = await service.ActivateTrial(request); ApiMetrics.Activations.Add(1, new KeyValuePair<string, object?>("outcome", "success")); return result;
        }).WithTags("Software clients").AddEndpointFilter<ActivationAdmissionFilter>();
    }
}
