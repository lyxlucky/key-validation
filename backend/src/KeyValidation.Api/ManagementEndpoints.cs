using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;

public static class ManagementEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);

    public static void MapManagementApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("License management").RequireAuthorization("Read");
        admin.MapGet("/batch-overview", async (LicensingService service, AppDbContext db, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? planId = null, string? createdBy = null, string? search = null, Guid? channelId = null) =>
        {
            var result = await service.BatchOverview(page, pageSize, applicationId, planId, createdBy, search, channelId);
            var ids = result.Items.Select(x => x.CreatedBy).Distinct().ToArray();
            var names = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName);
            return result with { Items = result.Items.Select(x => x with { CreatedByName = names.GetValueOrDefault(x.CreatedBy) }).ToArray() };
        }).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapGet("/batch-overview/{id:guid}", async (Guid id, LicensingService service, AppDbContext db) =>
        {
            var result = await service.BatchOverviewDetail(id);
            var name = await db.Users.AsNoTracking().Where(x => x.Id == result.CreatedBy).Select(x => x.UserName).SingleOrDefaultAsync();
            return result with { CreatedByName = name };
        }).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapGet("/expiring-licenses", (LicensingService service, int withinDays = 7, Guid? applicationId = null, int page = 1, int pageSize = 20, Guid? channelId = null) =>
            service.ExpiringLicenses(page, pageSize, withinDays, applicationId, channelId)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/license-operations", (LicenseOperationRequest request, HttpContext context, LicensingService service) =>
            service.LicenseOperation(request, ActorOf(context))).RequireAuthorization("Operate", ApplicationPermissions.ManageLicenses);
        admin.MapGet("/license-operations/{requestId:guid}", (Guid requestId, HttpContext context, LicensingService service) =>
            service.LicenseOperationReceipt(requestId, ActorOf(context))).RequireAuthorization(ApplicationPermissions.ReadLicenses);
    }
}
