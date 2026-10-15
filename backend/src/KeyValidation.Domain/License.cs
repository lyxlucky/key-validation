namespace KeyValidation.Domain;

public sealed partial class License
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid ApplicationId { get; private set; }
    public Guid? BatchId { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ChannelId { get; private set; }
    public string? KeyDigest { get; private set; }
    public string DigestVersion { get; private set; } = "";
    public string KeyHint { get; private set; } = "";
    public long DurationSeconds { get; private set; }
    public int MaxDevices { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }
    public string? Note { get; set; }
    public string[] Tags { get; set; } = [];
    public void AssignCustomer(Guid customerId, Guid? channelId)
    {
        if (CustomerId is not null || customerId == Guid.Empty) throw new RuleException("LICENSE_CUSTOMER_CONFLICT", 409);
        CustomerId = customerId; ChannelId = channelId;
    }
    public static License Issue(Guid app, Guid batch, string digest, string version, string hint, long duration, int devices,
        int? maxConcurrentSessions = null, int? activationValidityDays = null, string[]? features = null, DateTimeOffset? createdAt = null)
    {
        if (duration is < 1 or > 315360000 || devices is < 1 or > 100) throw new RuleException("VALIDATION_ERROR", 400);
        AuthorizationPolicies.Validate(maxConcurrentSessions ?? devices, activationValidityDays);
        var issued = createdAt ?? DateTimeOffset.UtcNow;
        return new() { ApplicationId = app, BatchId = batch, KeyDigest = digest, DigestVersion = version, KeyHint = hint, DurationSeconds = duration, MaxDevices = devices, RebindingEnabled = true,
            CreatedAt = issued, MaxConcurrentSessions = maxConcurrentSessions ?? devices, ActivateBefore = activationValidityDays is { } days ? issued.AddDays(days) : null, _features = AuthorizationPolicies.NormalizeFeatures(features) };
    }
    public void Activate(DateTimeOffset now)
    {
        EnsureUsable(now);
        if (ActivatedAt is not null) return;
        ActivatedAt = now; ExpiresAt = now.AddSeconds(DurationSeconds);
    }
    public void EnsureUsable(DateTimeOffset now)
    {
        if (DisabledAt is not null) throw new RuleException("LICENSE_DISABLED");
        if (FrozenAt is not null) throw new RuleException("LICENSE_FROZEN");
        if (ActivatedAt is null && ActivateBefore is { } deadline && now >= deadline) throw new RuleException("ACTIVATION_DEADLINE_PASSED");
        if (ExpiresAt is not null && now >= ExpiresAt) throw new RuleException("LICENSE_EXPIRED");
    }
    public string Status(DateTimeOffset now) => DisabledAt is not null ? "disabled" : FrozenAt is not null ? "frozen" : ActivatedAt is null ? ActivateBefore <= now ? "activation_expired" : "unused" : now < ExpiresAt ? "active" : "expired";
    public void EnsureDeviceCapacity(int activeBindings)
    {
        if (activeBindings >= MaxDevices) throw new RuleException("DEVICE_LIMIT_REACHED");
    }
    public void Disable(DateTimeOffset now) => DisabledAt ??= now;
    public void Enable() => DisabledAt = null;
    public void Extend(DateTimeOffset now, long additionalSeconds)
    {
        if (FrozenAt is not null) throw new RuleException("LICENSE_FROZEN");
        const long maximum = 315360000;
        if (additionalSeconds is < 3600 or > maximum || additionalSeconds % 3600 != 0)
            throw new RuleException("VALIDATION_ERROR", 400);
        if (ActivatedAt is null)
        {
            if (DurationSeconds > maximum - additionalSeconds) throw new RuleException("DURATION_LIMIT_EXCEEDED", 400);
            DurationSeconds += additionalSeconds;
            return;
        }
        var start = ExpiresAt is { } expiry && expiry > now ? expiry : now;
        if (start > now.AddSeconds(maximum - additionalSeconds)) throw new RuleException("DURATION_LIMIT_EXCEEDED", 400);
        ExpiresAt = start.AddSeconds(additionalSeconds);
    }
}

public sealed class LicenseSession
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid BindingId { get; private set; }
    public string TokenDigest { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset AbsoluteExpiresAt { get; private set; }
    public DateTimeOffset LeaseExpiresAt { get; private set; }
    public DateTimeOffset LastSeenAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public string? ClientVersion { get; set; }
    public static LicenseSession Start(Guid binding, string digest, DateTimeOffset now, DateTimeOffset licenseExpiry)
    {
        if (now >= licenseExpiry) throw new RuleException("LICENSE_EXPIRED");
        var absolute = Min(now.AddHours(8), licenseExpiry);
        return new() { BindingId = binding, TokenDigest = digest, CreatedAt = now, LastSeenAt = now, AbsoluteExpiresAt = absolute, LeaseExpiresAt = Min(now.AddSeconds(180), absolute) };
    }
    public void Heartbeat(DateTimeOffset now, DateTimeOffset licenseExpiry)
    {
        EnsureUsable(now, licenseExpiry);
        LastSeenAt = now; LeaseExpiresAt = Min(now.AddSeconds(180), Min(AbsoluteExpiresAt, licenseExpiry));
    }
    public void EnsureUsable(DateTimeOffset now, DateTimeOffset licenseExpiry)
    {
        if (RevokedAt is not null) throw new RuleException("SESSION_REVOKED", 401);
        if (now >= LeaseExpiresAt || now >= AbsoluteExpiresAt) throw new RuleException("SESSION_EXPIRED", 401);
        if (now >= licenseExpiry) throw new RuleException("LICENSE_EXPIRED");
    }
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}

public class RuleException(string code, int status = 403) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = status;
    public Guid? BatchId { get; init; }
}
