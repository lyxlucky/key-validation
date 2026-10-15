using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
public sealed partial class LicensingService
{
    private static string RebindingReason(string? reason)
    {
        reason = reason?.Trim();
        Require(reason is { Length: >= 1 and <= 500 });
        return reason!;
    }

    private async Task<License> LockRebindingLicense(Guid id)
    {
        var appId = await db.Licenses.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid? )x.ApplicationId).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        await LockApp(appId);
        await LockLicenses([id]);
        return await db.Licenses.SingleAsync(x => x.Id == id);
    }

    private async Task<RebindingStatus> RebindingStatusOf(License license, DateTimeOffset now)
    {
        var changes = db.RebindingEvents.AsNoTracking().Where(x => x.LicenseId == license.Id && x.Generation == license.RebindingGeneration && x.ChangedAt <= now);
        var start = now.AddDays(-license.RebindingWindowDays);
        var window = await changes.Where(x => x.ChangedAt > start).Select(x => x.ChangedAt).ToArrayAsync();
        var last = await changes.MaxAsync(x => (DateTimeOffset? )x.ChangedAt);
        return license.RebindingStatus(now, window, last);
    }

    private async Task<DevicePolicyDto> DevicePolicyOf(License license, DateTimeOffset now)
    {
        var status = await RebindingStatusOf(license, now);
        return new(license.Id, license.RebindingEnabled, license.RebindingMaxChanges, license.RebindingWindowDays, license.RebindingCooldownHours, status.UsedChanges, status.RemainingChanges, status.LastChangedAt, status.NextAllowedAt, now);
    }

    public async Task<DevicePolicyDto> DevicePolicy(Guid id)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var license = await LockRebindingLicense(id);
        var result = await DevicePolicyOf(license, Now);
        await transaction.CommitAsync();
        return result;
    }

    public async Task<DevicePolicyDto> UpdateDevicePolicy(Guid id, UpdateDevicePolicy request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var reason = RebindingReason(request.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var license = await LockRebindingLicense(id);
        var now = Now;
        var before = await DevicePolicyOf(license, now);
        license.ConfigureRebinding(request.Enabled, request.MaxChanges, request.WindowDays, request.CooldownHours);
        var after = await DevicePolicyOf(license, now);
        Audit(actor, "license.device-policy.update", id, new { reason, before, after });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return after;
    }

    public async Task<DevicePolicyDto> ResetDevicePolicy(Guid id, ResetDevicePolicy request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var reason = RebindingReason(request.Reason);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var license = await LockRebindingLicense(id);
        var now = Now;
        var before = await DevicePolicyOf(license, now);
        license.ResetRebinding();
        var after = await DevicePolicyOf(license, now);
        Audit(actor, "license.device-policy.reset", id, new { reason, generation = license.RebindingGeneration, before, after });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return after;
    }

    private Task<bool> ReleaseBindingUnderLock(License license, DeviceBinding binding, DateTimeOffset now) => BindingRelease.Execute(db, license, binding, now, events);
    public async Task ReleaseCurrentBinding(string token)
    {
        var location = await SessionLocation(null, secrets.DigestToken(token)) ?? throw new RuleException("SESSION_INVALID", 401);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var app = await LockApp(location.AppId);
        await LockLicenses([location.LicenseId]);
        if (app.DisabledAt is not null)
            throw new RuleException("APPLICATION_DISABLED");
        var license = await db.Licenses.SingleAsync(x => x.Id == location.LicenseId);
        var now = Now;
        license.EnsureUsable(now);
        var binding = await db.Bindings.SingleAsync(x => x.Id == location.BindingId);
        var session = await db.Sessions.SingleAsync(x => x.Id == location.SessionId);
        // A released binding revokes its tokens; possessing an old token is not proof of a live device.
        session.EnsureUsable(now, license.ExpiresAt!.Value);
        if (binding.ReleasedAt is not null)
            throw new RuleException("BINDING_RELEASED");
        await ReleaseBindingUnderLock(license, binding, now);
        db.AuthorizationEvents.Add(new() { ApplicationId = app.Id, LicenseId = license.Id, Outcome = "BINDING_RELEASED", CreatedAt = now });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }
}
