namespace KeyValidation.Domain;
public sealed class OrderLicense
{
    public Guid OrderId { get; set; }
    public Guid LicenseId { get; set; }
}
