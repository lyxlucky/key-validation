namespace KeyValidation.Domain;
public sealed class DeviceBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public string DeviceDigest { get; set; } = "";
    public string? DeviceLabel { get; set; }
    public DateTimeOffset BoundAt { get; set; }
    public DateTimeOffset? ReleasedAt { get; set; }
}
