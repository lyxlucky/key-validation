using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class CapabilityTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ExpiredLeaseDeniesAnOtherwiseGrantedFeature()
    {
        var clock = new Clock();
        var lease = new LeaseTracker(clock);
        var capabilities = new LicenseCapabilities(lease);
        capabilities.Update(["export"]);
        Assert.False(capabilities.Allows("export"));
        lease.Update(Now, Now.AddSeconds(30), Now.AddHours(1), TimeSpan.Zero);
        Assert.True(capabilities.Allows("export"));
        Assert.False(capabilities.Allows("admin"));
        clock.Advance(TimeSpan.FromSeconds(30));
        Assert.False(capabilities.Allows("export"));
    }

    [Fact]
    public void RefreshReplacesCapabilitiesAndMissingFeaturesFailClosed()
    {
        var lease = new LeaseTracker(new Clock());
        lease.Update(Now, Now.AddSeconds(30), Now.AddHours(1), TimeSpan.Zero);
        var capabilities = new LicenseCapabilities(lease);
        string[] features = ["export"];
        capabilities.Update(features);
        features[0] = "admin";
        Assert.True(capabilities.Allows("export"));
        Assert.False(capabilities.Allows("admin"));
        capabilities.Update(["preview"]);
        Assert.False(capabilities.Allows("export"));
        Assert.True(capabilities.Allows("preview"));
        capabilities.Update(null);
        Assert.False(capabilities.Allows("preview"));
    }

    private sealed class Clock : TimeProvider
    {
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public void Advance(TimeSpan amount) => ticks += amount.Ticks;
    }
}
