using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class LeaseTrackerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NetworkRoundTripIsDeductedFromGrantedLease()
    {
        var lease = new LeaseTracker(new ManualClock());
        lease.Update(Now, Now.AddSeconds(180), Now.AddHours(8), TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(175), lease.Remaining);
    }

    [Fact]
    public void AbsoluteExpiryCapsTheLease()
    {
        var lease = new LeaseTracker(new ManualClock());
        lease.Update(Now, Now.AddSeconds(180), Now.AddSeconds(20), TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(18), lease.Remaining);
    }

    [Fact]
    public void ChangingSystemWallClockCannotExtendLease()
    {
        var clock = new ManualClock();
        var lease = new LeaseTracker(clock);
        lease.Update(Now, Now.AddSeconds(180), Now.AddHours(8), TimeSpan.Zero);
        clock.Advance(TimeSpan.FromSeconds(120));
        clock.WallTime = Now.AddYears(-1);
        Assert.Equal(TimeSpan.FromSeconds(60), lease.Remaining);
    }

    [Fact]
    public void ExactExpiryStopsProtectedUse()
    {
        var clock = new ManualClock();
        var lease = new LeaseTracker(clock);
        lease.Update(Now, Now.AddSeconds(180), Now.AddHours(8), TimeSpan.Zero);
        Assert.True(lease.IsValid);
        clock.Advance(TimeSpan.FromSeconds(180));
        Assert.False(lease.IsValid);
        Assert.Equal(TimeSpan.Zero, lease.Remaining);
    }

    [Fact]
    public void DelayedResponseThatArrivesAfterLeaseIsRejected()
    {
        var lease = new LeaseTracker(new ManualClock());
        lease.Update(Now, Now.AddSeconds(3), Now.AddHours(8), TimeSpan.FromSeconds(4));
        Assert.False(lease.IsValid);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long ticks;
        public DateTimeOffset WallTime { get; set; } = Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => WallTime;
        public void Advance(TimeSpan amount) => ticks += amount.Ticks;
    }
}
