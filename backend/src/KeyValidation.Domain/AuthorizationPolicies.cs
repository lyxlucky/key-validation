using System.Text.RegularExpressions;

namespace KeyValidation.Domain;

public static class AuthorizationPolicies
{
    public static void Validate(int sessions, int? activationDays)
    {
        if (sessions is < 1 or > 100 || activationDays is < 1 or > 3650) throw new RuleException("VALIDATION_ERROR", 400);
    }

    public static string[] NormalizeFeatures(string[]? features)
    {
        var result = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in features ?? [])
        {
            var normalized = feature?.Trim().ToLowerInvariant();
            if (normalized is null || !Regex.IsMatch(normalized, "\\A[a-z][a-z0-9._-]{0,63}\\z")) throw new RuleException("VALIDATION_ERROR", 400);
            if (seen.Add(normalized)) result.Add(normalized);
            if (result.Count > 50) throw new RuleException("VALIDATION_ERROR", 400);
        }
        return result.ToArray();
    }
}

public sealed partial class License
{
    private string[] _features = [];
    public string[] Features { get => [.. _features]; private set => _features = [.. value]; }
    public int MaxConcurrentSessions { get; private set; } = 1;
    public DateTimeOffset? ActivateBefore { get; private set; }
    public DateTimeOffset? FrozenAt { get; private set; }
    public double? FrozenRemainingSeconds => FrozenAt is { } frozen ? (ExpiresAt!.Value - frozen).TotalSeconds : null;
    public bool IsTrial { get; private set; }

    public bool Freeze(DateTimeOffset now)
    {
        if (DisabledAt is not null) throw new RuleException("LICENSE_DISABLED");
        if (FrozenAt is not null) return false;
        if (ActivatedAt is null || ExpiresAt <= now) throw new RuleException("LICENSE_NOT_FREEZABLE");
        FrozenAt = now; return true;
    }
    public bool Unfreeze(DateTimeOffset now)
    {
        if (FrozenAt is null) return false;
        ExpiresAt = now + (ExpiresAt!.Value - FrozenAt.Value); FrozenAt = null; return true;
    }
    public void EnsureSessionCapacity(int liveSessions)
    {
        if (liveSessions >= MaxConcurrentSessions) throw new RuleException("SESSION_LIMIT_REACHED");
    }
    public static License ClaimTrial(Guid applicationId, long durationSeconds, string[] features, DateTimeOffset now)
    {
        if (durationSeconds is < 1 or > 2592000) throw new RuleException("VALIDATION_ERROR", 400);
        return new() { ApplicationId = applicationId, DurationSeconds = durationSeconds, MaxDevices = 1, MaxConcurrentSessions = 1,
            IsTrial = true, CreatedAt = now, _features = AuthorizationPolicies.NormalizeFeatures(features), RebindingEnabled = true };
    }
}

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

public sealed class TrialPolicy
{
    public Guid ApplicationId { get; set; }
    public bool IsEnabled { get; set; }
    public long DurationSeconds { get; set; } = 3600;
    public string[] Features { get; set; } = [];
}

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
