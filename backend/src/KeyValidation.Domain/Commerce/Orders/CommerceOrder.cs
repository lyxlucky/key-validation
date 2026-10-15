namespace KeyValidation.Domain;
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
