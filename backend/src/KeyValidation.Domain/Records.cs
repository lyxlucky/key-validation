namespace KeyValidation.Domain;

public sealed class SoftwareApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class LicenseBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid PlanId { get; set; }
    public int Quantity { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? Note { get; set; }
    public Guid RequestId { get; set; }
    public string RequestFingerprint { get; set; } = "";
}
public sealed class DeviceBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public string DeviceDigest { get; set; } = "";
    public string? DeviceLabel { get; set; }
    public DateTimeOffset BoundAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
public sealed class AdminAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid TargetId { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Category { get; set; } = "";
    public string Result { get; set; } = "";
    public string? ActorName { get; set; }
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public string? ApplicationName { get; set; }
    public string? SourceIp { get; set; }
    public string? Method { get; set; }
    public string? ErrorCategory { get; set; }
    public string Changes { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public string TraceId { get; set; } = "";
}
public sealed class AuthorizationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ApplicationId { get; set; }
    public Guid? LicenseId { get; set; }
    public string Outcome { get; set; } = "";
    public string? DeviceHint { get; set; }
    public string? TrustedCountryCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class LicenseOperationReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = "";
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
