namespace KeyValidation.Domain;

public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
    public string? AccessCodeDigest { get; set; }
    public Guid AccessVersion { get; set; } = Guid.NewGuid();
}

public sealed class CommerceOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid PlanId { get; set; }
    public string Type { get; set; } = "issue";
    public string Status { get; set; } = "pending";
    public int Quantity { get; set; }
    public long UnitPriceCents { get; set; }
    public Guid RequestId { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public Guid? RenewalLicenseId { get; set; }
    public Guid? BatchId { get; set; }
    public string PlanName { get; set; } = "";
    public long DurationSeconds { get; set; }
    public int MaxDevices { get; set; }
    public int MaxConcurrentSessions { get; set; }
    public int? ActivationValidityDays { get; set; }
    public string[] Features { get; set; } = [];
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? FulfilledBy { get; set; }
    public DateTimeOffset? FulfilledAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? DeliveryCiphertext { get; set; }
    public DateTimeOffset? DeliveryExpiresAt { get; set; }
}

public sealed class OrderLicense
{
    public Guid OrderId { get; set; }
    public Guid LicenseId { get; set; }
}
public sealed class OrderHistory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Action { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class CommerceReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = "";
    public string Operation { get; set; } = "";
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";
    public Guid[] ApplicationIds { get; set; } = [];
    public long RemainingQuota { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
public sealed class ChannelPrice
{
    public Guid ChannelId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid PlanId { get; set; }
    public long UnitPriceCents { get; set; }
}
public sealed class ChannelQuotaEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public string Kind { get; set; } = "";
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? OrderId { get; set; }
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
}

public sealed class PortalSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public string TokenDigest { get; set; } = "";
    public Guid AccessVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
public sealed class PortalRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid CustomerId { get; set; }
    public string Type { get; set; } = "";
    public Guid LicenseId { get; set; }
    public Guid? BindingId { get; set; }
    public string? Note { get; set; }
    public string Status { get; set; } = "pending";
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewReason { get; set; }
    public string? ReviewFingerprint { get; set; }
    public Guid? OrderId { get; set; }
}
public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Name { get; set; } = "";
    public string[] Permissions { get; set; } = [];
    public string SecretDigest { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
