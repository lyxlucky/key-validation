namespace KeyValidation.Domain;
public sealed class DeviceRebindingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public Guid BindingId { get; set; }
    public long Generation { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
