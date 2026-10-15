using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed partial class LicensingService
{
    public async Task FreezeLicense(Guid id, FreezeLicense request, bool freeze, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var reason = request.Reason?.Trim(); Require(reason is { Length: >= 1 and <= 500 });
        var appId = await db.Licenses.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.ApplicationId).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(appId); await LockLicenses([id]);
        var license = await db.Licenses.SingleAsync(x => x.Id == id); var now = Now;
        if (freeze)
        {
            if (license.Freeze(now))
            {
                db.LicenseFreezes.Add(new() { LicenseId = id, FrozenAt = now, RemainingSeconds = license.FrozenRemainingSeconds!.Value, FrozenBy = actor.Id, FreezeReason = reason! });
                await RevokeForLicenses([id]); Audit(actor, "license.freeze", id, new { reason, remainingSeconds = license.FrozenRemainingSeconds });
            }
        }
        else if (license.Unfreeze(now))
        {
            var history = await db.LicenseFreezes.SingleAsync(x => x.LicenseId == id && x.UnfrozenAt == null);
            history.UnfrozenAt = now; history.UnfrozenBy = actor.Id; history.UnfreezeReason = reason;
            Audit(actor, "license.unfreeze", id, new { reason, license.ExpiresAt });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task<PagedResult<LicenseFreezeDto>> FreezeHistory(Guid id, int page, int size)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        if (!await db.Licenses.AnyAsync(x => x.Id == id)) throw new RuleException("NOT_FOUND", 404);
        return await Page(db.LicenseFreezes.AsNoTracking().Where(x => x.LicenseId == id).OrderByDescending(x => x.FrozenAt).ThenBy(x => x.Id), page, size,
            x => new LicenseFreezeDto(x.Id, x.LicenseId, x.FrozenAt, x.UnfrozenAt, x.RemainingSeconds, x.FrozenBy, x.UnfrozenBy, x.FreezeReason, x.UnfreezeReason));
    }

    public async Task<TrialPolicyDto> TrialPolicy(Guid appId)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        if (!await db.Applications.AnyAsync(x => x.Id == appId)) throw new RuleException("NOT_FOUND", 404);
        var policy = await db.TrialPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == appId);
        return policy is null ? new(appId, false, 3600, []) : new(appId, policy.IsEnabled, policy.DurationSeconds, policy.Features);
    }

    public async Task<TrialPolicyDto> UpdateTrialPolicy(Guid appId, UpdateTrialPolicy request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.Administrator);
        Require(request.DurationSeconds is >= 1 and <= 2592000);
        var features = AuthorizationPolicies.NormalizeFeatures(request.Features);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(appId, true);
        var policy = await db.TrialPolicies.SingleOrDefaultAsync(x => x.ApplicationId == appId);
        var before = policy is null ? null : new { isEnabled = policy.IsEnabled, durationSeconds = policy.DurationSeconds, features = policy.Features.ToArray() };
        if (policy is null) { policy = new() { ApplicationId = appId }; db.TrialPolicies.Add(policy); }
        policy.IsEnabled = request.IsEnabled; policy.DurationSeconds = request.DurationSeconds; policy.Features = features;
        Audit(actor, "application.trial-policy.update", appId, new { before, after = new { isEnabled = policy.IsEnabled, durationSeconds = policy.DurationSeconds, features = policy.Features } });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(appId, policy.IsEnabled, policy.DurationSeconds, policy.Features);
    }
}
