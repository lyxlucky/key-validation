namespace KeyValidation.Domain;
public sealed class MonitoringIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
