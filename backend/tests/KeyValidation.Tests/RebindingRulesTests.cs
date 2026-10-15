using KeyValidation.Domain;
using Xunit;

namespace KeyValidation.Tests;

public sealed class RebindingRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private static License NewLicense() => License.Issue(Guid.NewGuid(), Guid.NewGuid(), "digest", "v1", "hint", 86400, 1);

    [Fact]
    public void IssuanceEnablesDefaultsWhileLegacyMaterializationRemainsUnrestricted()
    {
        Assert.False(new License().RebindingEnabled);
        var license = NewLicense();
        Assert.True(license.RebindingEnabled);
        Assert.Equal(3, license.RebindingMaxChanges);
        Assert.Equal(30, license.RebindingWindowDays);
        Assert.Equal(24, license.RebindingCooldownHours);
    }

    [Fact]
    public void WindowExcludesExactStartAndFutureEventsAndIncludesNow()
    {
        var license = NewLicense();
        license.ConfigureRebinding(true, 3, 30, 0);
        var status = license.RebindingStatus(Now, [Now.AddDays(-30), Now.AddDays(-30).AddTicks(1), Now, Now.AddTicks(1)], Now);
        Assert.Equal(2, status.UsedChanges);
        Assert.Equal(1, status.RemainingChanges);
        Assert.Null(status.ErrorCode);
        Assert.Null(status.NextAllowedAt);
    }

    [Fact]
    public void CooldownEndsExactlyAtBoundaryEvenWhenLastReleaseIsOutsideWindow()
    {
        var license = NewLicense();
        license.ConfigureRebinding(true, 3, 1, 48);
        var last = Now.AddHours(-48);
        var before = license.RebindingStatus(Now.AddTicks(-1), [], last);
        Assert.Equal("REBIND_COOLDOWN", before.ErrorCode);
        Assert.Equal(Now, before.NextAllowedAt);
        license.RebindingStatus(Now, [], last).EnsureAllowed();
    }

    [Fact]
    public void ReducingQuotaWaitsUntilEnoughOldEventsLeaveWindow()
    {
        var license = NewLicense();
        license.ConfigureRebinding(true, 2, 30, 0);
        var changes = new[] { Now.AddDays(-4), Now.AddDays(-3), Now.AddDays(-2), Now.AddDays(-1) };
        var status = license.RebindingStatus(Now, changes, changes[^1]);
        Assert.Equal(4, status.UsedChanges);
        Assert.Equal(0, status.RemainingChanges);
        Assert.Equal(Now.AddDays(28), status.NextAllowedAt);
        var error = Assert.Throws<RuleException>(status.EnsureAllowed);
        Assert.Equal("REBIND_LIMIT_REACHED", error.Code);
        Assert.Equal(409, error.StatusCode);
    }

    [Fact]
    public void NextAllowedTimeHonorsBothCooldownAndQuota()
    {
        var license = NewLicense();
        license.ConfigureRebinding(true, 1, 1, 48);
        var status = license.RebindingStatus(Now, [Now], Now);
        Assert.Equal(Now.AddDays(2), status.NextAllowedAt);
    }

    [Fact]
    public void DisabledPolicyRetainsUsageWithoutBlockingAndResetOnlyChangesGeneration()
    {
        var license = NewLicense(); license.Activate(Now);
        license.ConfigureRebinding(false, 1, 30, 24);
        var status = license.RebindingStatus(Now, [Now, Now], Now);
        Assert.Equal(2, status.UsedChanges);
        Assert.Null(status.RemainingChanges);
        Assert.Null(status.NextAllowedAt);
        status.EnsureAllowed();
        license.ResetRebinding(); license.ResetRebinding();
        Assert.Equal(2, license.RebindingGeneration);
        Assert.Equal(Now.AddDays(1), license.ExpiresAt);
        Assert.Equal(1, license.MaxDevices);
        Assert.False(license.RebindingEnabled);
    }
}
