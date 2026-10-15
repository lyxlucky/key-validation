using System.Data;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public sealed partial class LicensingService
{
    public async Task<ReportsCatalogDto> ReportsCatalog()
    {
        Demand(ApplicationPermissions.ReadReports);
        return await ReadCatalog();
    }

    public Task<ReportsCatalogDto> OperationsCatalog()
    {
        if (access is { IsAdministrativeRequest: true, IsAdministrator: false } && !access.Permissions.Any())
            throw new KeyValidation.Domain.RuleException("FORBIDDEN", 403);
        return ReadCatalog();
    }

    private async Task<ReportsCatalogDto> ReadCatalog()
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var applications = await db.Applications.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Select(x => new ReportApplicationDto(x.Id, x.Name)).ToArrayAsync();
        var plans = await planQueries.Catalog().OrderBy(x => x.Name).ThenBy(x => x.ApplicationId).ThenBy(x => x.Id).ToArrayAsync();
        await transaction.CommitAsync();
        return new(applications, plans);
    }

    public async Task<OperationsReport> OperationsReport(DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? applicationId = null, Guid? planId = null, Guid? channelId = null)
    {
        Demand(ApplicationPermissions.ReadReports);
        var now = Now;
        var end = to?.ToUniversalTime() ?? new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        var start = from?.ToUniversalTime() ?? end.AddDays(-30);
        Require(start < end && end - start <= TimeSpan.FromDays(366));
        // Every aggregate sees the same facts even when activations or renewals run concurrently.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var facts =
            from license in db.Licenses.AsNoTracking()join application in db.Applications.AsNoTracking()on license.ApplicationId equals application.Id
            join batch in db.Batches.AsNoTracking()on license.BatchId equals (Guid? )batch.Id into batches
            from batch in batches.DefaultIfEmpty()join plan in db.Plans.AsNoTracking()on batch.PlanId equals plan.Id into plans
            from plan in plans.DefaultIfEmpty()
            where (applicationId == null || license.ApplicationId == applicationId) && (planId == null || batch.PlanId == planId) && (channelId == null || license.ChannelId == channelId)select new
            {
                license.Id,
                license.ApplicationId,
                ApplicationName = application.Name,
                ApplicationDisabledAt = application.DisabledAt,
                PlanId = license.IsTrial ? null : (Guid? )batch.PlanId,
                PlanName = license.IsTrial ? null : plan.Name,
                license.CreatedAt,
                license.ActivatedAt,
                license.ExpiresAt,
                license.DisabledAt,
                license.FrozenAt
            };
        var counts = await facts.GroupBy(x => new { x.ApplicationId, x.ApplicationName, x.PlanId, x.PlanName }).Select(group => new { group.Key.ApplicationId, group.Key.ApplicationName, group.Key.PlanId, group.Key.PlanName, Issued = group.Count(x => x.CreatedAt >= start && x.CreatedAt < end), Activated = group.Count(x => x.ActivatedAt >= start && x.ActivatedAt < end), Expired = group.Count(x => x.FrozenAt == null && x.ExpiresAt >= start && x.ExpiresAt < end && x.ExpiresAt <= now) }).ToArrayAsync();
        var online =
            from session in db.Sessions.AsNoTracking()join binding in db.Bindings.AsNoTracking()on session.BindingId equals binding.Id
            join license in facts on binding.LicenseId equals license.Id
            where license.ApplicationDisabledAt == null && license.DisabledAt == null && license.FrozenAt == null && license.ExpiresAt > now && binding.ReleasedAt == null && session.RevokedAt == null && session.LeaseExpiresAt > now && session.AbsoluteExpiresAt > now
            select new
            {
                LicenseId = license.Id,
                license.ApplicationId,
                license.PlanId
            };
        var onlineCounts = await online.GroupBy(x => new { x.ApplicationId, x.PlanId }).Select(group => new { group.Key.ApplicationId, group.Key.PlanId, Licenses = group.Select(x => x.LicenseId).Distinct().Count(), Sessions = group.Count() }).ToArrayAsync();
        var onlineLookup = onlineCounts.ToDictionary(x => (x.ApplicationId, x.PlanId));
        var breakdown = counts.OrderBy(x => x.ApplicationName, StringComparer.Ordinal).ThenBy(x => x.PlanName, StringComparer.Ordinal).ThenBy(x => x.ApplicationId).ThenBy(x => x.PlanId).Select(x =>
        {
            var current = onlineLookup.GetValueOrDefault((x.ApplicationId, x.PlanId));
            return new OperationsBreakdown(x.ApplicationId, x.ApplicationName, x.PlanId, x.PlanName, x.Issued, x.Activated, x.Expired, current?.Licenses ?? 0, current?.Sessions ?? 0);
        }).ToArray();
        var issuedDays = await facts.Where(x => x.CreatedAt >= start && x.CreatedAt < end).GroupBy(x => DateOnly.FromDateTime(x.CreatedAt.UtcDateTime)).Select(group => new { Date = group.Key, Count = group.Count() }).ToArrayAsync();
        var activatedDays = await facts.Where(x => x.ActivatedAt >= start && x.ActivatedAt < end).GroupBy(x => DateOnly.FromDateTime(x.ActivatedAt!.Value.UtcDateTime)).Select(group => new { Date = group.Key, Count = group.Count() }).ToArrayAsync();
        var expiredDays = await facts.Where(x => x.FrozenAt == null && x.ExpiresAt >= start && x.ExpiresAt < end && x.ExpiresAt <= now).GroupBy(x => DateOnly.FromDateTime(x.ExpiresAt!.Value.UtcDateTime)).Select(group => new { Date = group.Key, Count = group.Count() }).ToArrayAsync();
        var issuedLookup = issuedDays.ToDictionary(x => x.Date, x => x.Count);
        var activatedLookup = activatedDays.ToDictionary(x => x.Date, x => x.Count);
        var expiredLookup = expiredDays.ToDictionary(x => x.Date, x => x.Count);
        var daily = new List<OperationsDay>();
        for (var day = DateOnly.FromDateTime(start.UtcDateTime); day <= DateOnly.FromDateTime(end.AddTicks(-1).UtcDateTime); day = day.AddDays(1))
            daily.Add(new(day, issuedLookup.GetValueOrDefault(day), activatedLookup.GetValueOrDefault(day), expiredLookup.GetValueOrDefault(day)));
        var totals = new OperationsCounts(breakdown.Sum(x => x.Issued), breakdown.Sum(x => x.Activated), breakdown.Sum(x => x.Expired), breakdown.Sum(x => x.OnlineLicenses), breakdown.Sum(x => x.OnlineSessions));
        await transaction.CommitAsync();
        return new(start, end, now, "UTC", totals, daily, breakdown);
    }
}
