namespace KeyValidation.Domain;
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
