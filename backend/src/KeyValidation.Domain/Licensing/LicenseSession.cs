namespace KeyValidation.Domain;
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
        if (now >= licenseExpiry)
            throw new RuleException("LICENSE_EXPIRED");
        var absolute = Min(now.AddHours(8), licenseExpiry);
        return new()
        {
            BindingId = binding,
            TokenDigest = digest,
            CreatedAt = now,
            LastSeenAt = now,
            AbsoluteExpiresAt = absolute,
            LeaseExpiresAt = Min(now.AddSeconds(180), absolute)
        };
    }

    public void Heartbeat(DateTimeOffset now, DateTimeOffset licenseExpiry)
    {
        EnsureUsable(now, licenseExpiry);
        LastSeenAt = now;
        LeaseExpiresAt = Min(now.AddSeconds(180), Min(AbsoluteExpiresAt, licenseExpiry));
    }

    public void EnsureUsable(DateTimeOffset now, DateTimeOffset licenseExpiry)
    {
        if (RevokedAt is not null)
            throw new RuleException("SESSION_REVOKED", 401);
        if (now >= LeaseExpiresAt || now >= AbsoluteExpiresAt)
            throw new RuleException("SESSION_EXPIRED", 401);
        if (now >= licenseExpiry)
            throw new RuleException("LICENSE_EXPIRED");
    }

    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
