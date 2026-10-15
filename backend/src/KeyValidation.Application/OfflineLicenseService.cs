using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class OfflineLicenseService(ILicensingData db, ApplicationAccessScope access, ISecretService secrets, IOfflineLicenseSigner signer, TimeProvider clock)
{
    private DateTimeOffset Now { get { var now = clock.GetUtcNow(); return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero); } }
    private static RuleException Missing() => new("NOT_FOUND", 404);
    public OfflineConfigurationDto Configuration() { access.Require(ApplicationPermissions.ReadLicenses); return signer.Configuration(); }

    public async Task<PagedResult<OfflineLicenseDto>> List(Guid? applicationId, Guid? licenseId, int page, int pageSize)
    {
        access.Require(ApplicationPermissions.ReadLicenses);
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100) throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.OfflineLicenses.AsNoTracking();
        if (applicationId is not null) query = query.Where(x => x.ApplicationId == applicationId);
        if (licenseId is not null) query = query.Where(x => x.LicenseId == licenseId);
        var count = await query.CountAsync(); var now = Now;
        var rows = await (from record in query join license in db.Licenses.AsNoTracking() on record.LicenseId equals license.Id
            join binding in db.Bindings.AsNoTracking() on record.BindingId equals binding.Id
            join application in db.Applications.AsNoTracking() on record.ApplicationId equals application.Id
            orderby record.IssuedAt descending, record.Id select new { record, license, binding, application })
            .Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(rows.Select(x => Dto(x.record, x.application, x.license, x.binding, now)).ToArray(), count, page, pageSize);
    }

    public async Task<SignedOfflineLicenseDto> Sign(SignOfflineLicense request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageLicenses);
        if (request.LicenseId == Guid.Empty || request.BindingId == Guid.Empty || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 256 ||
            (request.Hours ?? OfflineLicenseLimits.DefaultHours) is < 1 or > OfflineLicenseLimits.MaximumHours) throw new RuleException("VALIDATION_ERROR", 400);
        var configuration = signer.Configuration(); if (!configuration.Configured) throw new RuleException(configuration.ErrorCode ?? "OFFLINE_UNCONFIGURED", 503);
        var appId = await db.Licenses.AsNoTracking().Where(x => x.Id == request.LicenseId).Select(x => (Guid?)x.ApplicationId).SingleOrDefaultAsync() ?? throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync();
        var application = await LockApplication(appId); await LockLicense(request.LicenseId);
        var license = await db.Licenses.SingleOrDefaultAsync(x => x.Id == request.LicenseId) ?? throw Missing();
        var now = Now; RequireUsable(application, license, now);
        var binding = await db.Bindings.SingleOrDefaultAsync(x => x.Id == request.BindingId && x.LicenseId == license.Id) ?? throw Missing();
        if (binding.ReleasedAt is not null) throw new RuleException("BINDING_RELEASED");
        if (!EqualDigest(binding.DeviceDigest, secrets.DigestDevice(appId, request.DeviceId))) throw new RuleException("OFFLINE_DEVICE_MISMATCH");
        var record = new OfflineLicenseRecord { ApplicationId = appId, ChannelId = license.ChannelId, LicenseId = license.Id, BindingId = binding.Id,
            Features = license.Features, IssuedAt = now, NotBefore = now, ExpiresAt = Min(now.AddHours(request.Hours ?? OfflineLicenseLimits.DefaultHours), license.ExpiresAt!.Value), IssuedBy = actor.Id };
        var payload = new OfflineLicensePayload(1, record.Serial, application.Code, license.Id, binding.Id, record.Features,
            now, now, record.ExpiresAt, DeviceHash(application.Code, request.DeviceId));
        var signed = signer.Sign(payload); record.KeyId = signed.KeyId; record.ContentSha256 = ContentHash(signed.Content);
        var old = await db.OfflineLicenses.Where(x => x.BindingId == binding.Id && x.RevokedAt == null).ToArrayAsync();
        foreach (var previous in old)
        {
            previous.RevokedAt = now; previous.RevocationReason = "superseded";
            Audit(actor, "offline-license.supersede", previous, new { status = "superseded", supersededBy = record.Id }, now);
        }
        // Release the active-record unique slot while keeping the license lock.
        await db.SaveChangesAsync();
        db.OfflineLicenses.Add(record); Audit(actor, "offline-license.issue", record, new { serial = record.Serial, licenseId = license.Id, bindingId = binding.Id, kid = record.KeyId, expiresAt = record.ExpiresAt, features = record.Features }, now);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(Dto(record, application, license, binding, now), signed.Content);
    }

    public async Task Revoke(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageLicenses);
        var location = await db.OfflineLicenses.AsNoTracking().Where(x => x.Id == id).Select(x => new { x.ApplicationId, x.LicenseId }).SingleOrDefaultAsync() ?? throw Missing();
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApplication(location.ApplicationId); await LockLicense(location.LicenseId);
        var record = await db.OfflineLicenses.SingleOrDefaultAsync(x => x.Id == id) ?? throw Missing();
        if (record.RevokedAt is null)
        {
            var now = Now; record.RevokedAt = now; record.RevocationReason = "revoked";
            Audit(actor, "offline-license.revoke", record, new { status = "revoked", record.Serial, record.LicenseId, record.BindingId }, now);
        }
        // Also recheck authority on an idempotent revocation.
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }

    public async Task<VerifiedOfflineLicenseDto> Verify(VerifyOfflineLicense request)
    {
        if (string.IsNullOrWhiteSpace(request.AppCode) || request.AppCode.Length > 64 || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 256) throw new RuleException("VALIDATION_ERROR", 400);
        var payload = signer.Verify(request.LicenseContent);
        if (payload.ApplicationCode != ApplicationCode.Normalize(request.AppCode)) throw new RuleException("OFFLINE_APPLICATION_MISMATCH");
        if (!EqualDigest(payload.DeviceHash, DeviceHash(payload.ApplicationCode, request.DeviceId))) throw new RuleException("OFFLINE_DEVICE_MISMATCH");
        var location = await db.OfflineLicenses.AsNoTracking().Where(x => x.Serial == payload.Serial).Select(x => new { x.Id, x.ApplicationId, x.LicenseId }).SingleOrDefaultAsync() ?? throw new RuleException("OFFLINE_LICENSE_INVALID");
        await using var tx = await db.Database.BeginTransactionAsync(); var application = await LockApplication(location.ApplicationId); await LockLicense(location.LicenseId);
        var now = Now;
        if (now < payload.NotBefore || now < payload.IssuedAt) throw new RuleException("OFFLINE_LICENSE_NOT_YET_VALID");
        if (now >= payload.ExpiresAt) throw new RuleException("OFFLINE_LICENSE_EXPIRED");
        var record = await db.OfflineLicenses.SingleAsync(x => x.Id == location.Id);
        if (!EqualDigest(record.ContentSha256, ContentHash(request.LicenseContent))) throw new RuleException("OFFLINE_LICENSE_INVALID");
        if (record.RevokedAt is not null) throw new RuleException(record.RevocationReason == "superseded" ? "OFFLINE_LICENSE_SUPERSEDED" : "OFFLINE_LICENSE_REVOKED");
        var license = await db.Licenses.SingleAsync(x => x.Id == record.LicenseId); RequireUsable(application, license, now);
        if ((await db.Bindings.SingleAsync(x => x.Id == record.BindingId)).ReleasedAt is not null) throw new RuleException("BINDING_RELEASED");
        await tx.CommitAsync();
        return new(payload.Serial, payload.ApplicationCode, payload.LicenseId, payload.BindingId, payload.Features, payload.IssuedAt, payload.NotBefore, payload.ExpiresAt, now, "active", OfflineLicenseLimits.Limitations);
    }

    private async Task<SoftwareApplication> LockApplication(Guid id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {id} FOR SHARE");
        return await db.Applications.SingleOrDefaultAsync(x => x.Id == id) ?? throw Missing();
    }
    private async Task LockLicense(Guid id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Licenses\" WHERE \"Id\" = {id} FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Bindings\" WHERE \"LicenseId\" = {id} ORDER BY \"Id\" FOR UPDATE");
    }
    private static void RequireUsable(SoftwareApplication application, License license, DateTimeOffset now)
    {
        if (application.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        if (license.IsTrial) throw new RuleException("OFFLINE_FORMAL_LICENSE_REQUIRED");
        license.EnsureUsable(now);
        if (license.ActivatedAt is null || license.ExpiresAt is null) throw new RuleException("OFFLINE_ACTIVATION_REQUIRED");
    }
    private void Audit(Actor actor, string action, OfflineLicenseRecord record, object facts, DateTimeOffset now)
        => db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = record.Id, ApplicationId = record.ApplicationId, ChannelId = record.ChannelId,
            Changes = JsonSerializer.Serialize(facts, new JsonSerializerOptions(JsonSerializerDefaults.Web)), CreatedAt = now, TraceId = actor.TraceId });
    private static OfflineLicenseDto Dto(OfflineLicenseRecord record, SoftwareApplication app, License license, DeviceBinding binding, DateTimeOffset now)
    {
        var status = record.RevokedAt is not null ? record.RevocationReason == "superseded" ? "superseded" : "revoked" : now >= record.ExpiresAt ? "expired" :
            app.DisabledAt is not null || binding.ReleasedAt is not null || license.Status(now) != "active" ? "unavailable" : "active";
        return new(record.Id, record.Serial, record.ApplicationId, app.Code, record.LicenseId, license.KeyHint, record.BindingId, binding.DeviceLabel, record.KeyId, record.Features,
            record.IssuedAt, record.NotBefore, record.ExpiresAt, record.RevokedAt, record.RevocationReason, record.IssuedBy, status, OfflineLicenseLimits.Limitations);
    }
    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
    private static string DeviceHash(string code, string deviceId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ApplicationCode.Normalize(code) + "\n" + deviceId)));
    private static string ContentHash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    private static bool EqualDigest(string first, string second) => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
}
