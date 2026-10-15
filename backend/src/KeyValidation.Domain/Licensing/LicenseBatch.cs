namespace KeyValidation.Domain;
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
