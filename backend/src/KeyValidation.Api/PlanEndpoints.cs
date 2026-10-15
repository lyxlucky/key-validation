using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;

namespace KeyValidation.Api;

public static class PlanEndpoints
{
    private static Actor ActorOf(HttpContext c) => new(
        c.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new RuleException("UNAUTHENTICATED", 401),
        c.TraceIdentifier);

    public static void MapPlanApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").WithTags("Plan catalog")
            .RequireAuthorization("Read");
        admin.MapGet("/plans", (PlanCatalogService s, int page = 1,
            int pageSize = 20, string? search = null, bool? isEnabled = null) =>
            s.List(page, pageSize, search, isEnabled)).RequireAuthorization("Admin");
        admin.MapGet("/plans/{id:guid}", (Guid id, PlanCatalogService s) =>
            s.Get(id)).RequireAuthorization("Admin");
        admin.MapPost("/plans", async (CreatePlan request, HttpContext c, PlanCatalogService s) =>
        {
            var result = await s.Create(request, ActorOf(c));
            return TypedResults.Created($"/api/admin/plans/{result.Id}", result);
        }).RequireAuthorization("Admin");
        admin.MapPatch("/plans/{id:guid}", (Guid id, UpdatePlan request,
            HttpContext c, PlanCatalogService s) =>
            s.Update(id, request, ActorOf(c))).RequireAuthorization("Admin");
        admin.MapGet("/plans/{id:guid}/applications", (Guid id, PlanCatalogService s) =>
            s.Applications(id)).RequireAuthorization("Admin");
        admin.MapGet("/applications/{id:guid}/plan-references",
            (Guid id, PlanCatalogService s, int page = 1, int pageSize = 20) =>
            s.References(id, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPut("/applications/{id:guid}/plan-references/{planId:guid}",
            (Guid id, Guid planId, SetPlanReference request, HttpContext c, PlanCatalogService s) =>
            s.SetReference(id, planId, request, ActorOf(c))).RequireAuthorization("Admin");

        admin.MapGet("/applications/{id:guid}/plans",
            (Guid id, LicensingService s, int page = 1, int pageSize = 20) =>
            s.Plans(id, page, pageSize)).RequireAuthorization(ApplicationPermissions.ReadLicenses);
        admin.MapPost("/applications/{id:guid}/plans",
            async (Guid id, CreatePlan request, HttpContext c, PlanCatalogService s) =>
            {
                var result = await s.CreateForApplication(id, request, ActorOf(c));
                return TypedResults.Created($"/api/admin/plans/{result.Id}", result);
            }).RequireAuthorization("Admin");
    }
}
