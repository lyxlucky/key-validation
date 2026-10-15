namespace KeyValidation.Domain;
public sealed class RiskPolicy
{
    public Guid ApplicationId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ActivationThreshold { get; set; } = 10;
    public int ActivationWindowMinutes { get; set; } = 10;
    public int RebindingThreshold { get; set; } = 3;
    public int RebindingWindowMinutes { get; set; } = 60;
    public int RegionWindowHours { get; set; } = 24;
    public DateTimeOffset? ScannedThrough { get; set; }
}
