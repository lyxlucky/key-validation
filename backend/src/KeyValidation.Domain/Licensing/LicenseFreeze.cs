using System.Text.RegularExpressions;

namespace KeyValidation.Domain;
public sealed class LicenseFreeze
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public DateTimeOffset FrozenAt { get; set; }
    public DateTimeOffset? UnfrozenAt { get; set; }
    public double RemainingSeconds { get; set; }
    public string FrozenBy { get; set; } = "";
    public string? UnfrozenBy { get; set; }
    public string FreezeReason { get; set; } = "";
    public string? UnfreezeReason { get; set; }
}
