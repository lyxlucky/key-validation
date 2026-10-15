using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class RiskService(AppDbContext db, ApplicationAccessScope access, IDurableEventPublisher publisher, TimeProvider clock)
{
    public async Task<RiskPolicyDto> Policy(Guid applicationId)
    {
        access.Require(ApplicationPermissions.Administrator);
        await RequireApplication(applicationId);
        return Dto(await db.RiskPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId) ?? new RiskPolicy { ApplicationId = applicationId });
    }

    public async Task<RiskPolicyDto> SetPolicy(Guid applicationId, UpdateRiskPolicy request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        if (request.ActivationThreshold is < 1 or > 10000 || request.RebindingThreshold is < 1 or > 1000 || request.ActivationWindowMinutes is < 1 or > 1440 || request.RebindingWindowMinutes is < 1 or > 10080 || request.RegionWindowHours is < 1 or > 720)
            throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 1)");
        await RequireApplication(applicationId);
        var policy = await db.RiskPolicies.SingleOrDefaultAsync(x => x.ApplicationId == applicationId);
        var before = policy is null ? null : Dto(policy);
        if (policy is null)
        {
            policy = new()
            {
                ApplicationId = applicationId
            };
            db.RiskPolicies.Add(policy);
        }

        policy.Enabled = request.Enabled;
        policy.ActivationThreshold = request.ActivationThreshold;
        policy.ActivationWindowMinutes = request.ActivationWindowMinutes;
        policy.RebindingThreshold = request.RebindingThreshold;
        policy.RebindingWindowMinutes = request.RebindingWindowMinutes;
        policy.RegionWindowHours = request.RegionWindowHours;
        ReliabilityRules.Audit(db, actor, "risk-policy.update", applicationId, applicationId, clock.GetUtcNow(), new { before, after = Dto(policy) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(policy);
    }

    public async Task<PagedResult<RiskEventDto>> List(int page, int pageSize, Guid? applicationId, string? status)
    {
        access.Require("risk.review");
        ReliabilityRules.Page(page, pageSize);
        if (status is not null && status is not "open" and not "confirmed" and not "dismissed")
            throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.RiskEvents.AsNoTracking().Where(x => (applicationId == null || x.ApplicationId == applicationId) && (status == null || x.Status == status));
        var count = await query.CountAsync();
        var records = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(records.Select(Dto).ToArray(), count, page, pageSize);
    }

    public async Task<RiskEventDto> Detail(Guid id)
    {
        access.Require("risk.review");
        return Dto(await db.RiskEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404));
    }

    public async Task<RiskEventDto> Review(Guid id, ReviewRiskEvent request, Actor actor)
    {
        access.RequireWrite("risk.review");
        if (request.Status is not "open" and not "confirmed" and not "dismissed" || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 1000)
            throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_risk_events\" WHERE \"Id\" = {id} FOR UPDATE");
        var record = await db.RiskEvents.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        var before = new
        {
            record.Status,
            record.ReviewReason,
            record.ReviewedBy,
            record.ReviewedAt
        };
        record.Status = request.Status;
        record.ReviewReason = request.Reason.Trim();
        record.ReviewedBy = actor.Id;
        record.ReviewedAt = clock.GetUtcNow();
        ReliabilityRules.Audit(db, actor, "risk.review", id, record.ApplicationId, clock.GetUtcNow(), new { before, after = new { record.Status, record.ReviewReason, record.ReviewedBy, record.ReviewedAt } });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(record);
    }

    // Caller holds the scanner lock and exclusive application locks through
    // commit, and captured now after locking this exact set. Apps created later
    // wait for the next pass instead of receiving an unprotected cursor.
    internal async Task<int> Scan(Guid[] applications, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var applicationId in applications)
        {
            var policy = await db.RiskPolicies.SingleOrDefaultAsync(x => x.ApplicationId == applicationId, cancellationToken);
            if (policy is null)
            {
                policy = new()
                {
                    ApplicationId = applicationId
                };
                db.RiskPolicies.Add(policy);
            }

            var since = policy.ScannedThrough ?? now.AddHours(-24);
            if (!policy.Enabled)
            {
                policy.ScannedThrough = now;
                continue;
            }

            var activationWindow = TimeSpan.FromMinutes(policy.ActivationWindowMinutes);
            var regionWindow = TimeSpan.FromHours(policy.RegionWindowHours);
            var releaseWindow = TimeSpan.FromMinutes(policy.RebindingWindowMinutes);
            var lookback = since - TimeSpan.FromTicks(Math.Max(activationWindow.Ticks, regionWindow.Ticks));
            var activations = await db.AuthorizationEvents.AsNoTracking().Where(x => x.ApplicationId == applicationId && x.LicenseId != null && x.Outcome == "ACTIVATION_SUCCEEDED" && x.CreatedAt >= lookback && x.CreatedAt < now).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
            foreach (var license in activations.GroupBy(x => x.LicenseId!.Value))
            {
                count += await Detect(applicationId, license.Key, "activation_burst", license.Select(x => new Evidence(x.CreatedAt, null)).ToArray(), activationWindow, policy.ActivationThreshold, since, now, cancellationToken);
                count += await Detect(applicationId, license.Key, "country_change", license.Where(x => x.TrustedCountryCode != null).Select(x => new Evidence(x.CreatedAt, x.TrustedCountryCode)).ToArray(), regionWindow, 2, since, now, cancellationToken);
            }

            var releaseStart = since - releaseWindow;
            var releases = await (
                from binding in db.Bindings.AsNoTracking()join license in db.Licenses.AsNoTracking()on binding.LicenseId equals license.Id
                where license.ApplicationId == applicationId && binding.ReleasedAt >= releaseStart && binding.ReleasedAt < now
                orderby binding.ReleasedAt, binding.Id
                select new
                {
                    binding.LicenseId,
                    binding.ReleasedAt
                }

            ).ToArrayAsync(cancellationToken);
            foreach (var license in releases.GroupBy(x => x.LicenseId))
                count += await Detect(applicationId, license.Key, "rebinding_burst", license.Select(x => new Evidence(x.ReleasedAt!.Value, null)).ToArray(), releaseWindow, policy.RebindingThreshold, since, now, cancellationToken);
            policy.ScannedThrough = now;
        }

        return count;
    }

    private sealed record Evidence(DateTimeOffset At, string? Country);
    private async Task<int> Detect(Guid applicationId, Guid licenseId, string rule, Evidence[] evidence, TimeSpan window, int threshold, DateTimeOffset since, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (evidence.Length < threshold)
            return 0;
        var existing = await db.RiskEvents.Where(x => x.LicenseId == licenseId && x.Rule == rule && x.WindowEnd >= since - window).ToListAsync(cancellationToken);
        var first = 0;
        var created = 0;
        for (var last = 0; last < evidence.Length; last++)
        {
            var current = evidence[last];
            if (current.At < since)
                continue;
            while (first < last && evidence[first].At <= current.At - window)
                first++;
            if (last - first + 1 < threshold || existing.Any(x => current.At >= x.WindowStart && current.At < x.WindowEnd))
                continue;
            var countries = rule == "country_change" ? evidence[first..(last + 1)].Select(x => x.Country!).Distinct().Order().ToArray() : [];
            if (rule == "country_change" && countries.Length < 2)
                continue;
            var record = new RiskEvent
            {
                ApplicationId = applicationId,
                LicenseId = licenseId,
                Rule = rule,
                Summary = rule switch
                {
                    "activation_burst" => "短时间内多次成功激活",
                    "rebinding_burst" => "短时间内多次释放设备",
                    _ => "可信代理报告不同国家代码"
                },
                EvidenceJson = JsonSerializer.Serialize(new { count = last - first + 1, threshold, countries }),
                WindowStart = evidence[first].At,
                WindowEnd = evidence[first].At + window,
                CreatedAt = now
            };
            db.RiskEvents.Add(record);
            existing.Add(record);
            created++;
            var channelId = await db.Licenses.Where(x => x.Id == licenseId).Select(x => x.ChannelId).SingleOrDefaultAsync(cancellationToken);
            await publisher.PublishAsync(applicationId, record.Id, OperationsEventTypes.RiskDetected, JsonSerializer.Serialize(new { riskEventId = record.Id, licenseId, rule, count = last - first + 1 }), "risk:" + record.Id, cancellationToken, channelId);
        }

        return created;
    }

    private async Task RequireApplication(Guid id)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == id))
            throw new RuleException("NOT_FOUND", 404);
    }

    private static RiskPolicyDto Dto(RiskPolicy x) => new(x.ApplicationId, x.Enabled, x.ActivationThreshold, x.ActivationWindowMinutes, x.RebindingThreshold, x.RebindingWindowMinutes, x.RegionWindowHours);
    private static RiskEventDto Dto(RiskEvent x) => new(x.Id, x.ApplicationId, x.LicenseId, x.Rule, x.Summary, x.EvidenceJson, x.WindowStart, x.WindowEnd, x.Status, x.CreatedAt, x.ReviewedAt, x.ReviewedBy, x.ReviewReason);
}
