using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

// Every authorization mutation follows Application -> License -> Binding -> Session.
// License row locks serialize device counting and prevent a heartbeat racing revocation.
public sealed partial class LicensingService(ILicensingData db, ISecretService secrets, TimeProvider clock, PlanAvailability planAvailability, PlanCatalogQueries planQueries, ApplicationAccessScope? access = null,
    IDurableEventPublisher? events = null, ClientRequestFacts? requestFacts = null)
{
    private void Demand(string permission) => access?.Require(permission);
    private void DemandWrite(string permission) => access?.RequireWrite(permission);
    // PostgreSQL timestamps store microseconds; return exactly the value we persist.
    private DateTimeOffset Now { get { var now = clock.GetUtcNow(); return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero); } }
    private static void Require(bool condition) { if (!condition) throw new RuleException("VALIDATION_ERROR", 400); }
    private static string Name(string? value) { Require(!string.IsNullOrWhiteSpace(value) && value.Length <= 120); return value!.Trim(); }
    private void Audit(Actor actor, string action, Guid target, object? changes = null) => db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = target, Changes = JsonSerializer.Serialize(changes ?? new { }), CreatedAt = Now, TraceId = actor.TraceId });
    private async Task<SoftwareApplication> LockApp(Guid id, bool exclusive = false)
    {
        if (exclusive) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {id} FOR UPDATE");
        else await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {id} FOR SHARE");
        return await db.Applications.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    }
    private async Task LockLicenses(Guid[] ids)
    {
        if (ids.Length == 0) return;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Licenses\" WHERE \"Id\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Bindings\" WHERE \"LicenseId\" = ANY({ids}) ORDER BY \"Id\" FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT s.\"Id\" FROM \"Sessions\" s JOIN \"Bindings\" b ON b.\"Id\" = s.\"BindingId\" WHERE b.\"LicenseId\" = ANY({ids}) ORDER BY s.\"Id\" FOR UPDATE OF s");
    }
    private async Task RevokeForLicenses(Guid[] ids)
    {
        var bindings = db.Bindings.Where(b => ids.Contains(b.LicenseId)).Select(b => b.Id);
        var sessions = await db.Sessions.Where(s => bindings.Contains(s.BindingId) && s.RevokedAt == null).OrderBy(s => s.Id).ToListAsync();
        foreach (var session in sessions) session.Revoke(Now);
    }
    public async Task<ApplicationDto> CreateApplication(CreateApplication request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.Administrator);
        var name = Name(request.Name); var code = ApplicationCode.Normalize(request.Code);
        Require(Regex.IsMatch(code, "^[a-z0-9_-]{3,64}$"));
        await using var tx = await db.Database.BeginTransactionAsync();
        var app = new SoftwareApplication { Code = code, Name = name, CreatedAt = Now };
        db.Applications.Add(app); Audit(actor, "application.create", app.Id, new { code, name });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return AppDto(app);
    }
    public async Task SetApplication(Guid id, bool disabled, Actor actor)
    {
        DemandWrite(ApplicationPermissions.Administrator);
        await using var tx = await db.Database.BeginTransactionAsync();
        var app = await LockApp(id, true);
        var ids = await db.Licenses.Where(x => x.ApplicationId == id).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        await LockLicenses(ids);
        app.DisabledAt = disabled ? app.DisabledAt ?? Now : null;
        if (disabled) await RevokeForLicenses(ids);
        Audit(actor, disabled ? "application.disable" : "application.enable", id);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task<IssuanceDto> Issue(IssueBatch request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.IssueLicenses);
        if (access?.ChannelId != null) throw new RuleException("CHANNEL_ORDER_REQUIRED", 403);
        Require(request.ApplicationId != Guid.Empty && request.PlanId != Guid.Empty && request.RequestId != Guid.Empty && request.Quantity is >= 1 and <= 1000 && (request.Note?.Length ?? 0) <= 500);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { request.ApplicationId, request.PlanId, request.Quantity, request.Note })));
        var advisoryKey = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(actor.Id + ":" + request.RequestId)));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({advisoryKey})");
        var previous = await db.Batches.SingleOrDefaultAsync(x => x.CreatedBy == actor.Id && x.RequestId == request.RequestId);
        if (previous is not null)
        {
            if (access is not null) await access.RecheckWrite(ApplicationPermissions.IssueLicenses);
            throw new RuleException(previous.RequestFingerprint == fingerprint ? "BATCH_ALREADY_CREATED" : "IDEMPOTENCY_CONFLICT", 409) { BatchId = previous.Id };
        }
        var app = await LockApp(request.ApplicationId);
        if (app.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        var plan = await planAvailability.Require(app.Id, request.PlanId);
        var batch = new LicenseBatch { ApplicationId = app.Id, PlanId = plan.Id, Quantity = request.Quantity, CreatedBy = actor.Id, CreatedAt = Now, Note = request.Note, RequestId = request.RequestId, RequestFingerprint = fingerprint };
        db.Batches.Add(batch); var issued = new List<IssuedKey>(request.Quantity);
        for (var i = 0; i < request.Quantity; i++)
        {
            var key = secrets.GenerateLicense(); var license = License.Issue(app.Id, batch.Id, key.Digest, key.Version, key.Hint, plan.DurationSeconds, plan.MaxDevices,
                plan.MaxConcurrentSessions, plan.ActivationValidityDays, plan.Features, batch.CreatedAt);
            db.Licenses.Add(license); issued.Add(new(license.Id, key.Plaintext, key.Hint));
        }
        Audit(actor, "batch.create", batch.Id, new { batch.ApplicationId, batch.PlanId, batch.Quantity, batch.RequestId });
        if (events is not null) await events.PublishAsync(app.Id, batch.Id, OperationsEventTypes.BatchCompleted,
            JsonSerializer.Serialize(new { action = "issue", succeeded = request.Quantity, failed = 0 }), "batch-issued:" + batch.Id);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(BatchDtoOf(batch), issued);
    }
    public async Task SetLicense(Guid id, bool disabled, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var appId = await db.Licenses.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.ApplicationId).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(appId); await LockLicenses([id]);
        var license = await db.Licenses.SingleAsync(x => x.Id == id);
        if (disabled) { license.Disable(Now); await RevokeForLicenses([id]); } else license.Enable();
        Audit(actor, disabled ? "license.disable" : "license.enable", id); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task DisableBatch(Guid id, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var appId = await db.Batches.AsNoTracking().Where(x => x.Id == id).Select(x => (Guid?)x.ApplicationId).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(appId);
        var ids = await db.Licenses.Where(x => x.BatchId == id).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync(); await LockLicenses(ids);
        foreach (var license in await db.Licenses.Where(x => x.BatchId == id).ToListAsync()) license.Disable(Now);
        await RevokeForLicenses(ids); Audit(actor, "batch.disable", id); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task ReleaseBinding(Guid id, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var info = await (from binding in db.Bindings.AsNoTracking() join license in db.Licenses.AsNoTracking() on binding.LicenseId equals license.Id where binding.Id == id select new { license.ApplicationId, LicenseId = license.Id }).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(info.ApplicationId); await LockLicenses([info.LicenseId]);
        var licenseRecord = await db.Licenses.SingleAsync(x => x.Id == info.LicenseId);
        var bindingRecord = await db.Bindings.SingleAsync(x => x.Id == id);
        if (await ReleaseBindingUnderLock(licenseRecord, bindingRecord, Now)) Audit(actor, "binding.release", id);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task RevokeSession(Guid id, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        var info = await SessionLocation(id, null) ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(info.AppId); await LockLicenses([info.LicenseId]);
        (await db.Sessions.SingleAsync(x => x.Id == id)).Revoke(Now); Audit(actor, "session.revoke", id);
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    public async Task<ActivationDto> Activate(ActivateLicense request)
    {
        Require(!string.IsNullOrWhiteSpace(request.AppCode) && request.AppCode.Length <= 64 && !string.IsNullOrWhiteSpace(request.DeviceId) && request.DeviceId.Length <= 256 && (request.DeviceLabel?.Length ?? 0) <= 120);
        try { return await ActivateTransaction(request); }
        catch (RuleException error) { db.ChangeTracker.Clear(); db.AuthorizationEvents.Add(new() { Outcome = error.Code, CreatedAt = Now }); await db.SaveChangesAsync(); throw; }
    }
    private async Task<ActivationDto> ActivateTransaction(ActivateLicense request)
    {
        var key = secrets.DigestLicense(request.LicenseKey);
        var code = ApplicationCode.Normalize(request.AppCode);
        var appId = await db.Applications.AsNoTracking().Where(x => x.Code == code).Select(x => (Guid?)x.Id).SingleOrDefaultAsync() ?? throw new RuleException("LICENSE_INVALID");
        await using var tx = await db.Database.BeginTransactionAsync(); var app = await LockApp(appId);
        if (app.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        var version = ClientVersionRules.Evaluate(await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == appId), request.ClientVersion);
        var id = await db.Licenses.AsNoTracking().Where(x => x.ApplicationId == appId && x.KeyDigest == key.Digest).Select(x => (Guid?)x.Id).SingleOrDefaultAsync() ?? throw new RuleException("LICENSE_INVALID");
        var deviceDigest = secrets.DigestDevice(appId, request.DeviceId);
        await LockTrialDevice(appId, deviceDigest);
        var claim = await db.TrialClaims.SingleOrDefaultAsync(x => x.ApplicationId == appId && x.DeviceDigest == deviceDigest);
        await LockLicenses(claim is null ? [id] : [id, claim.LicenseId]);
        var license = await db.Licenses.SingleAsync(x => x.Id == id);
        var result = await StartActivation(license, deviceDigest, request.DeviceLabel, version);
        if (claim is { UpgradedAt: null })
        {
            claim.UpgradedAt = result.ServerTime; claim.UpgradedToLicenseId = id;
            await RevokeForLicenses([claim.LicenseId]);
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return result;
    }
    private async Task<ActivationDto> StartActivation(License license, string deviceDigest, string? deviceLabel, ClientVersionInfo version)
    {
        var id = license.Id; var now = Now; license.EnsureUsable(now);
        var binding = await db.Bindings.SingleOrDefaultAsync(x => x.LicenseId == id && x.DeviceDigest == deviceDigest && x.ReleasedAt == null);
        if (binding is null) license.EnsureDeviceCapacity(await db.Bindings.CountAsync(x => x.LicenseId == id && x.ReleasedAt == null));
        var existingBindingId = binding?.Id;
        var activeBindings = db.Bindings.Where(x => x.LicenseId == id && x.ReleasedAt == null && (existingBindingId == null || x.Id != existingBindingId)).Select(x => x.Id);
        license.EnsureSessionCapacity(await db.Sessions.CountAsync(x => activeBindings.Contains(x.BindingId) && x.RevokedAt == null && x.LeaseExpiresAt > now && x.AbsoluteExpiresAt > now));
        if (binding is null)
        {
            binding = new() { LicenseId = id, DeviceDigest = deviceDigest, DeviceLabel = deviceLabel, BoundAt = now }; db.Bindings.Add(binding);
        }
        license.Activate(now);
        foreach (var old in await db.Sessions.Where(x => x.BindingId == binding.Id && x.RevokedAt == null).ToListAsync()) old.Revoke(now);
        // Flush revocations before the insert to satisfy the partial unique index.
        await db.SaveChangesAsync();
        var token = secrets.GenerateSession(); var session = LicenseSession.Start(binding.Id, token.Digest, now, license.ExpiresAt!.Value); db.Sessions.Add(session);
        session.ClientVersion = version.ClientVersion;
        db.AuthorizationEvents.Add(new() { ApplicationId = license.ApplicationId, LicenseId = id, Outcome = "ACTIVATION_SUCCEEDED", DeviceHint = deviceDigest[..8], TrustedCountryCode = requestFacts?.TrustedCountryCode, CreatedAt = now });
        return new(token.Token, now, license.ExpiresAt.Value, session.LeaseExpiresAt, session.AbsoluteExpiresAt) { LicenseId = id, IsTrial = license.IsTrial, Features = license.Features, Version = version };
    }
    private sealed record Location(Guid AppId, Guid LicenseId, Guid BindingId, Guid SessionId);
    private Task<Location?> SessionLocation(Guid? id, string? digest) => (from session in db.Sessions.AsNoTracking() join binding in db.Bindings.AsNoTracking() on session.BindingId equals binding.Id join license in db.Licenses.AsNoTracking() on binding.LicenseId equals license.Id where (id != null && session.Id == id) || (digest != null && session.TokenDigest == digest) select new Location(license.ApplicationId, license.Id, binding.Id, session.Id)).SingleOrDefaultAsync();
    public async Task<HeartbeatDto> Heartbeat(string token, string? clientVersion = null)
    {
        try { return await HeartbeatTransaction(token, clientVersion); }
        catch (RuleException error) { db.ChangeTracker.Clear(); db.AuthorizationEvents.Add(new() { Outcome = error.Code, CreatedAt = Now }); await db.SaveChangesAsync(); throw; }
    }
    private async Task<HeartbeatDto> HeartbeatTransaction(string token, string? clientVersion)
    {
        var location = await SessionLocation(null, secrets.DigestToken(token)) ?? throw new RuleException("SESSION_INVALID", 401);
        await using var tx = await db.Database.BeginTransactionAsync(); var app = await LockApp(location.AppId); await LockLicenses([location.LicenseId]);
        if (app.DisabledAt is not null) throw new RuleException("APPLICATION_DISABLED");
        var license = await db.Licenses.SingleAsync(x => x.Id == location.LicenseId); var now = Now; license.EnsureUsable(now);
        if ((await db.Bindings.SingleAsync(x => x.Id == location.BindingId)).ReleasedAt is not null) throw new RuleException("BINDING_RELEASED");
        var session = await db.Sessions.SingleAsync(x => x.Id == location.SessionId); session.EnsureUsable(now, license.ExpiresAt!.Value);
        var version = ClientVersionRules.Evaluate(await db.VersionPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == location.AppId), clientVersion ?? session.ClientVersion);
        session.ClientVersion = version.ClientVersion; session.Heartbeat(now, license.ExpiresAt.Value);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(now, license.ExpiresAt.Value, session.LeaseExpiresAt, session.AbsoluteExpiresAt) { LicenseId = license.Id, IsTrial = license.IsTrial, Features = license.Features, Version = version };
    }
    public async Task LogoutClient(string token)
    {
        var location = await SessionLocation(null, secrets.DigestToken(token)) ?? throw new RuleException("SESSION_INVALID", 401);
        await using var tx = await db.Database.BeginTransactionAsync(); await LockApp(location.AppId); await LockLicenses([location.LicenseId]);
        (await db.Sessions.SingleAsync(x => x.Id == location.SessionId)).Revoke(Now); await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
