using System.Text.RegularExpressions;

namespace KeyValidation.Domain;
public sealed class TrialClaim
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string DeviceDigest { get; set; } = "";
    public Guid LicenseId { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
    public Guid? UpgradedToLicenseId { get; set; }
    public DateTimeOffset? UpgradedAt { get; set; }
}
