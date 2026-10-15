using System.Text.RegularExpressions;

namespace KeyValidation.Domain;
public sealed partial class License
{
    private string[] _features = [];
    public string[] Features { get => [.._features]; private set => _features = [..value]; }
    public int MaxConcurrentSessions { get; private set; } = 1;
    public DateTimeOffset? ActivateBefore { get; private set; }
    public DateTimeOffset? FrozenAt { get; private set; }
    public double? FrozenRemainingSeconds => FrozenAt is { } frozen ? (ExpiresAt!.Value - frozen).TotalSeconds : null;
    public bool IsTrial { get; private set; }

    public bool Freeze(DateTimeOffset now)
    {
        if (DisabledAt is not null)
            throw new RuleException("LICENSE_DISABLED");
        if (FrozenAt is not null)
            return false;
        if (ActivatedAt is null || ExpiresAt <= now)
            throw new RuleException("LICENSE_NOT_FREEZABLE");
        FrozenAt = now;
        return true;
    }

    public bool Unfreeze(DateTimeOffset now)
    {
        if (FrozenAt is null)
            return false;
        ExpiresAt = now + (ExpiresAt!.Value - FrozenAt.Value);
        FrozenAt = null;
        return true;
    }

    public void EnsureSessionCapacity(int liveSessions)
    {
        if (liveSessions >= MaxConcurrentSessions)
            throw new RuleException("SESSION_LIMIT_REACHED");
    }

    public static License ClaimTrial(Guid applicationId, long durationSeconds, string[] features, DateTimeOffset now)
    {
        if (durationSeconds is < 1 or > 2592000)
            throw new RuleException("VALIDATION_ERROR", 400);
        return new()
        {
            ApplicationId = applicationId,
            DurationSeconds = durationSeconds,
            MaxDevices = 1,
            MaxConcurrentSessions = 1,
            IsTrial = true,
            CreatedAt = now,
            _features = AuthorizationPolicies.NormalizeFeatures(features),
            RebindingEnabled = true
        };
    }
}
