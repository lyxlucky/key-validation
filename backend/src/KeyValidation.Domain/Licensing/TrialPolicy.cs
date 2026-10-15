using System.Text.RegularExpressions;

namespace KeyValidation.Domain;
public sealed class TrialPolicy
{
    public Guid ApplicationId { get; set; }
    public bool IsEnabled { get; set; }
    public long DurationSeconds { get; set; } = 3600;
    public string[] Features { get; set; } = [];
}
