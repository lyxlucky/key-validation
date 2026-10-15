using KeyValidation.Domain;
using Xunit;

namespace KeyValidation.Tests;

public class LicenseRenewalTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);
    private static License Issue(long duration = 86400) => License.Issue(Guid.NewGuid(), Guid.NewGuid(), "digest", "v1", "hint", duration, 1);

    [Fact] public void UnusedRenewalIncreasesActivationDuration()
    {
        var license = Issue();
        license.Extend(Now, 7200);
        Assert.Equal(93600, license.DurationSeconds);
        Assert.Null(license.ActivatedAt);
        Assert.Null(license.ExpiresAt);
        license.Activate(Now.AddDays(1));
        Assert.Equal(Now.AddDays(2).AddHours(2), license.ExpiresAt);
    }

    [Fact] public void ActiveRenewalExtendsOldExpiryAndPreservesIssuanceSnapshot()
    {
        var license = Issue(); license.Activate(Now);
        license.Extend(Now.AddHours(1), 7200);
        Assert.Equal(Now.AddDays(1).AddHours(2), license.ExpiresAt);
        Assert.Equal(Now, license.ActivatedAt);
        Assert.Equal(86400, license.DurationSeconds);
    }

    [Theory] [InlineData(24)] [InlineData(48)]
    public void ExpiredRenewalStartsAtCurrentTime(int hours)
    {
        var license = Issue(); license.Activate(Now);
        license.Extend(Now.AddHours(hours), 3600);
        Assert.Equal(Now.AddHours(hours + 1), license.ExpiresAt);
        Assert.Equal(Now, license.ActivatedAt);
    }

    [Fact] public void DisabledRenewalPreservesDisabledState()
    {
        var license = Issue(); license.Activate(Now); license.Disable(Now);
        license.Extend(Now.AddDays(2), 3600);
        Assert.Equal(Now, license.DisabledAt);
        Assert.Equal("disabled", license.Status(Now.AddDays(2)));
        Assert.Equal(Now.AddDays(2).AddHours(1), license.ExpiresAt);
    }

    [Theory] [InlineData(0)] [InlineData(-3600)] [InlineData(3601)] [InlineData(315363600)] [InlineData(long.MaxValue)]
    public void InvalidExtensionDoesNotMutateLicense(long seconds)
    {
        var license = Issue();
        Assert.Equal("VALIDATION_ERROR", Assert.Throws<RuleException>(() => license.Extend(Now, seconds)).Code);
        Assert.Equal(86400, license.DurationSeconds);
        Assert.Null(license.ExpiresAt);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public void TenYearLimitRejectsEntireLicenseChange(bool activated)
    {
        var license = Issue(315360000);
        if (activated) license.Activate(Now);
        var expiry = license.ExpiresAt;
        Assert.Equal("DURATION_LIMIT_EXCEEDED", Assert.Throws<RuleException>(() => license.Extend(Now, 3600)).Code);
        Assert.Equal(315360000, license.DurationSeconds);
        Assert.Equal(expiry, license.ExpiresAt);
    }
}
