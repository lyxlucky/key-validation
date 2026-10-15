namespace KeyValidation.Domain;
public sealed class LicensePlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public long DurationSeconds { get; set; }
    public int MaxDevices { get; set; } = 1;
    public int MaxConcurrentSessions { get; set; } = 1;
    public int? ActivationValidityDays { get; set; }
    public string[] Features { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
