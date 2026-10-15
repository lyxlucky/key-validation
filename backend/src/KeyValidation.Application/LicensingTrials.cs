using System.Security.Cryptography;
using System.Text;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed partial class LicensingService
{
    // Both trial and formal paths hold this lock after the app lock and before
    // discovering/locking licenses, so first claim cannot race an upgrade.
    private async Task LockTrialDevice(Guid appId, string digest)
    {
        var key = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("trial:" + appId + ":" + digest)));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})");
    }

    public async Task<ActivationDto> ActivateTrial(ActivateTrial request)
    {
        Require(!string.IsNullOrWhiteSpace(request.AppCode) && request.AppCode.Length <= 64 && !string.IsNullOrWhiteSpace(request.DeviceId) && request.DeviceId.Length <= 256 && (request.DeviceLabel?.Length ?? 0) <= 120);
        try { return await ActivateTrialTransaction(request); }
        catch (RuleException error) { db.ChangeTracker.Clear(); db.AuthorizationEvents.Add(new() { Outcome = error.Code, CreatedAt = Now }); await db.SaveChangesAsync(); throw; }
    }

    private async Task<ActivationDto> ActivateTrialTransaction(ActivateTrial request)
    {
        var code = ApplicationCode.Normalize(request.AppCode);
        var appId = await db.Applications.AsNoTracking().Where(x => x.Code == code).Select(x => (Guid?)x.Id).SingleOrDefaultAsync() ?? throw new RuleException("LICENSE_INVALID");
        await using var tx = await db.Database.BeginTransactionAsync(); var app = await LockApp(appId);
        if (app.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        var version = ClientVersionRules.Evaluate(await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == appId), request.ClientVersion);
        var policy = await db.TrialPolicies.SingleOrDefaultAsync(x => x.ApplicationId == appId);
        if (policy is not { IsEnabled: true }) throw new RuleException("TRIAL_DISABLED");
        var deviceDigest = secrets.DigestDevice(appId, request.DeviceId);
        await LockTrialDevice(appId, deviceDigest);
        var claim = await db.TrialClaims.SingleOrDefaultAsync(x => x.ApplicationId == appId && x.DeviceDigest == deviceDigest);
        License license;
        if (claim is null)
        {
            if (await (from binding in db.Bindings join formal in db.Licenses on binding.LicenseId equals formal.Id
                where formal.ApplicationId == appId && !formal.IsTrial && binding.DeviceDigest == deviceDigest select binding.Id).AnyAsync())
                throw new RuleException("TRIAL_UPGRADED");
            license = License.ClaimTrial(appId, policy.DurationSeconds, policy.Features, Now);
            db.Licenses.Add(license);
            claim = new() { ApplicationId = appId, DeviceDigest = deviceDigest, LicenseId = license.Id, ClaimedAt = Now }; db.TrialClaims.Add(claim);
        }
        else
        {
            if (claim.UpgradedAt is not null) throw new RuleException("TRIAL_UPGRADED");
            await LockLicenses([claim.LicenseId]); license = await db.Licenses.SingleAsync(x => x.Id == claim.LicenseId);
            if (license.DisabledAt is null && license.FrozenAt is null && license.ExpiresAt <= Now) throw new RuleException("TRIAL_EXPIRED");
        }
        var result = await StartActivation(license, deviceDigest, request.DeviceLabel, version);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return result;
    }
}
