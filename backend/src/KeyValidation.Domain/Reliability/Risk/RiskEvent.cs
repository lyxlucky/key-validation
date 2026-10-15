namespace KeyValidation.Domain;
public sealed class RiskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid LicenseId { get; set; }
    public string Rule { get; set; } = "";
    public string Summary { get; set; } = "";
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewReason { get; set; }
}
