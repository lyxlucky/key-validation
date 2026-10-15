using KeyValidation.Domain;
using Xunit;

namespace KeyValidation.Tests;

public class LicenseRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private static License NewLicense(long duration = 86400, int devices = 1) => License.Issue(Guid.NewGuid(), Guid.NewGuid(), "digest", "v1", "V1-…ABCD", duration, devices);

    [Fact] public void RepeatedActivationDoesNotExtendExpiry()
    {
        var license = NewLicense();
        license.Activate(Now);
        license.Activate(Now.AddHours(1));
        Assert.Equal(Now.AddDays(1), license.ExpiresAt);
    }
    [Fact] public void ExactExpiryRejectsActivation()
    {
        var license = NewLicense(); license.Activate(Now);
        Assert.Equal("active", license.Status(Now.AddDays(1).AddTicks(-1)));
        Assert.Equal("expired", license.Status(Now.AddDays(1)));
        Assert.Equal("LICENSE_EXPIRED", Assert.Throws<RuleException>(() => license.Activate(Now.AddDays(1))).Code);
    }
    [Fact] public void CapacityCannotBeExceeded()
    {
        var license = NewLicense(); license.EnsureDeviceCapacity(0);
        Assert.Equal("DEVICE_LIMIT_REACHED", Assert.Throws<RuleException>(() => license.EnsureDeviceCapacity(1)).Code);
    }
    [Fact] public void ExpiredLeaseCannotBeRevived()
    {
        var session = LicenseSession.Start(Guid.NewGuid(), "digest", Now, Now.AddDays(1));
        Assert.Equal(Now.AddSeconds(180), session.LeaseExpiresAt);
        Assert.Equal("SESSION_EXPIRED", Assert.Throws<RuleException>(() => session.Heartbeat(Now.AddSeconds(180), Now.AddDays(1))).Code);
    }
    [Fact] public void RevokedSessionCannotBeRevived()
    {
        var session = LicenseSession.Start(Guid.NewGuid(), "digest", Now, Now.AddDays(1));
        session.Revoke(Now);
        Assert.Equal("SESSION_REVOKED", Assert.Throws<RuleException>(() => session.Heartbeat(Now.AddSeconds(1), Now.AddDays(1))).Code);
    }
    [Fact] public void LeaseCannotExceedLicenseExpiry()
    {
        var session = LicenseSession.Start(Guid.NewGuid(), "digest", Now, Now.AddSeconds(90));
        Assert.Equal(Now.AddSeconds(90), session.LeaseExpiresAt);
    }
    [Fact] public void HeartbeatCannotExtendAbsoluteSessionLifetime()
    {
        var session = LicenseSession.Start(Guid.NewGuid(), "digest", Now, Now.AddDays(1));
        for (var minutes = 1; minutes < 480; minutes++) session.Heartbeat(Now.AddMinutes(minutes), Now.AddDays(1));
        Assert.Equal(Now.AddHours(8), session.LeaseExpiresAt);
        Assert.Throws<RuleException>(() => session.Heartbeat(Now.AddHours(8), Now.AddDays(1)));
    }
}
