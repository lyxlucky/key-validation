namespace KeyValidation.Domain;
public sealed class AuthorizationEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ApplicationId { get; set; }
    public Guid? LicenseId { get; set; }
    public string Outcome { get; set; } = "";
    public string? DeviceHint { get; set; }
    public string? TrustedCountryCode { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
