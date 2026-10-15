namespace KeyValidation.Client;

public sealed class LeaseTracker(TimeProvider clock)
{
    private long receivedAt;
    private TimeSpan granted;
    public TimeSpan Remaining
    {
        get
        {
            var remaining = granted - clock.GetElapsedTime(receivedAt);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
    public bool IsValid => Remaining > TimeSpan.Zero;
    public void Update(DateTimeOffset serverTime, DateTimeOffset leaseExpiresAt,
        DateTimeOffset absoluteExpiresAt, TimeSpan requestDuration)
    {
        var expiry = leaseExpiresAt < absoluteExpiresAt ? leaseExpiresAt : absoluteExpiresAt;
        granted = expiry - serverTime - requestDuration;
        receivedAt = clock.GetTimestamp();
    }
}
