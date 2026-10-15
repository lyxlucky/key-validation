namespace KeyValidation.Domain;
public sealed class MonitoringSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InstanceId { get; set; } = "";
    public DateTimeOffset SampledAt { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public bool DatabaseAvailable { get; set; }
    public long RequestCount { get; set; }
    public long ServerErrorCount { get; set; }
    public double? ErrorRate { get; set; }
    public double? P95LatencyMs { get; set; }
    public string RoutesJson { get; set; } = "[]";
}
