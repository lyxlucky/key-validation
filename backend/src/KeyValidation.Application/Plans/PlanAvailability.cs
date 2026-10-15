// Application/PlanAvailability.cs
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public sealed class PlanAvailability(ILicensingData db)
{
    public async Task<LicensePlan> Require(Guid applicationId, Guid planId, bool requireEnabled = true)
    {
        if (applicationId == Guid.Empty || planId == Guid.Empty)
            throw new RuleException("VALIDATION_ERROR", 400);
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Plan lookup requires the caller's transaction.");
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "Id" FROM "t_plans" WHERE "Id" = {planId} FOR SHARE""");
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT "PlanId" FROM "t_application_plans" WHERE "ApplicationId" = {applicationId} AND "PlanId" = {planId} FOR SHARE""");
        var reference = await db.ApplicationPlans.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId && x.PlanId == planId);
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId);
        if (reference is null || plan is null)
            throw new RuleException("NOT_FOUND", 404);
        if (requireEnabled && !plan.IsEnabled)
            throw new RuleException("PLAN_DISABLED");
        if (requireEnabled && !reference.IsEnabled)
            throw new RuleException("APPLICATION_PLAN_DISABLED", 409);
        return plan;
    }
}
