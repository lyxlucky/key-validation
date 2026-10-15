namespace KeyValidation.Domain;

public sealed partial class License
{
    // Existing rows and direct materialization remain unrestricted. Issue opts in.
    public bool RebindingEnabled { get; private set; }
    public int RebindingMaxChanges { get; private set; } = 3;
    public int RebindingWindowDays { get; private set; } = 30;
    public int RebindingCooldownHours { get; private set; } = 24;
    public long RebindingGeneration { get; private set; }

    public void ConfigureRebinding(bool enabled, int maxChanges, int windowDays, int cooldownHours)
    {
        if (maxChanges is < 1 or > 100 || windowDays is < 1 or > 365 || cooldownHours is < 0 or > 8760)
            throw new RuleException("VALIDATION_ERROR", 400);
        RebindingEnabled = enabled;
        RebindingMaxChanges = maxChanges;
        RebindingWindowDays = windowDays;
        RebindingCooldownHours = cooldownHours;
    }

    public void ResetRebinding() => RebindingGeneration = checked(RebindingGeneration + 1);

    public RebindingStatus RebindingStatus(DateTimeOffset now, IEnumerable<DateTimeOffset> changes, DateTimeOffset? lastChangedAt)
    {
        var window = changes.Where(x => x > now.AddDays(-RebindingWindowDays) && x <= now).Order().ToArray();
        DateTimeOffset? quotaEnds = window.Length >= RebindingMaxChanges
            ? window[window.Length - RebindingMaxChanges].AddDays(RebindingWindowDays) : null;
        DateTimeOffset? cooldownEnds = lastChangedAt?.AddHours(RebindingCooldownHours);
        if (cooldownEnds <= now) cooldownEnds = null;
        DateTimeOffset? next = quotaEnds;
        if (cooldownEnds is not null && (next is null || cooldownEnds > next)) next = cooldownEnds;
        return new(window.Length, RebindingEnabled ? Math.Max(0, RebindingMaxChanges - window.Length) : null,
            lastChangedAt, RebindingEnabled ? next : null,
            RebindingEnabled && quotaEnds is not null ? "REBIND_LIMIT_REACHED" :
            RebindingEnabled && cooldownEnds is not null ? "REBIND_COOLDOWN" : null);
    }
}

public sealed record RebindingStatus(int UsedChanges, int? RemainingChanges, DateTimeOffset? LastChangedAt, DateTimeOffset? NextAllowedAt, string? ErrorCode)
{
    public void EnsureAllowed()
    {
        if (ErrorCode is not null) throw new RuleException(ErrorCode, 409);
    }
}

public sealed class DeviceRebindingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LicenseId { get; set; }
    public Guid BindingId { get; set; }
    public long Generation { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
}
