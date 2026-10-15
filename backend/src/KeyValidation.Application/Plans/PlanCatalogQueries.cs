// Application/PlanCatalogQueries.cs
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public sealed class PlanCatalogQueries(ILicensingData db)
{
    public IQueryable<PlanDto> Scoped() =>
        from reference in db.ApplicationPlans.AsNoTracking()join plan in db.Plans.AsNoTracking()on reference.PlanId equals plan.Id
        join application in db.Applications.AsNoTracking()on reference.ApplicationId equals application.Id
        select new PlanDto(plan.Id, application.Id, plan.Name, plan.DurationSeconds, plan.MaxDevices, application.DisabledAt == null && plan.IsEnabled && reference.IsEnabled, plan.CreatedAt, plan.MaxConcurrentSessions, plan.ActivationValidityDays, plan.Features)
        {
            Id = plan.Id,
            ApplicationId = application.Id,
            Name = plan.Name,
            CreatedAt = plan.CreatedAt,
            IsEnabled = application.DisabledAt == null && plan.IsEnabled && reference.IsEnabled
        };
    public IQueryable<ReportPlanDto> Catalog() => Scoped().Select(x => new ReportPlanDto(x.Id, x.ApplicationId, x.Name, x.IsEnabled) { Id = x.Id, ApplicationId = x.ApplicationId, Name = x.Name, IsEnabled = x.IsEnabled });
}
