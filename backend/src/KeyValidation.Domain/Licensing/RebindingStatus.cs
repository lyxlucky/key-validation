namespace KeyValidation.Domain;
public sealed record RebindingStatus(int UsedChanges, int? RemainingChanges, DateTimeOffset? LastChangedAt, DateTimeOffset? NextAllowedAt, string? ErrorCode)
{
    public void EnsureAllowed()
    {
        if (ErrorCode is not null)
            throw new RuleException(ErrorCode, 409);
    }
}
