using KeyValidation.Domain;
using Xunit;

namespace KeyValidation.Tests;

public sealed class AuthorizationPolicyRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 0, 0, 0, TimeSpan.Zero);
    private static License Card(int? deadline = null, string[]? features = null) => License.Issue(Guid.NewGuid(), Guid.NewGuid(), "digest", "v1", "hint", 86400, 3, 1, deadline, features, Now);

    [Fact] public void FreezePreservesFractionalRemainingDurationAndRepeatedOperationsAreIdempotent()
    {
        var card = Card(); card.Activate(Now);
        Assert.True(card.Freeze(Now.AddMilliseconds(1234)));
        Assert.Equal(86398.766, card.FrozenRemainingSeconds!.Value, 6);
        Assert.False(card.Freeze(Now.AddDays(3)));
        Assert.True(card.Unfreeze(Now.AddDays(4)));
        Assert.Equal(Now.AddDays(5).AddMilliseconds(-1234), card.ExpiresAt);
        Assert.False(card.Unfreeze(Now.AddDays(6)));
        Assert.Equal(Now.AddDays(5).AddMilliseconds(-1234), card.ExpiresAt);
    }
    [Fact] public void DisabledTakesPrecedenceAndUnfreezingDoesNotEnableOrRestoreFrozenRenewal()
    {
        var card = Card(); card.Activate(Now); card.Freeze(Now.AddSeconds(1)); card.Disable(Now.AddSeconds(2));
        Assert.Equal("LICENSE_DISABLED", Assert.Throws<RuleException>(() => card.EnsureUsable(Now)).Code);
        Assert.Equal("LICENSE_DISABLED", Assert.Throws<RuleException>(() => card.Freeze(Now)).Code);
        Assert.Equal("LICENSE_FROZEN", Assert.Throws<RuleException>(() => card.Extend(Now, 3600)).Code);
        card.Enable(); Assert.Equal("LICENSE_FROZEN", Assert.Throws<RuleException>(() => card.EnsureUsable(Now)).Code);
        card.Disable(Now); card.Unfreeze(Now.AddHours(1));
        Assert.Equal("disabled", card.Status(Now.AddHours(1)));
    }
    [Fact] public void UnusedAndExpiredCardsCannotFreeze()
    {
        var card = Card(); Assert.Equal("LICENSE_NOT_FREEZABLE", Assert.Throws<RuleException>(() => card.Freeze(Now)).Code);
        card.Activate(Now); Assert.Equal("LICENSE_NOT_FREEZABLE", Assert.Throws<RuleException>(() => card.Freeze(Now.AddDays(1))).Code);
    }
    [Fact] public void DeadlineIsExclusiveAndOnlyAppliesBeforeFirstActivation()
    {
        var expired = Card(1); expired.Extend(Now, 3600);
        Assert.Equal("ACTIVATION_DEADLINE_PASSED", Assert.Throws<RuleException>(() => expired.Activate(Now.AddDays(1))).Code);
        Assert.Equal("activation_expired", expired.Status(Now.AddDays(1)));
        var active = Card(1); active.Activate(Now.AddDays(1).AddTicks(-1)); active.EnsureUsable(Now.AddDays(1));
        Assert.Equal("active", active.Status(Now.AddDays(1)));
    }
    [Fact] public void FeatureSnapshotNormalizesAndCannotBeChangedThroughInputOrReturnedArray()
    {
        var input = new[] { " Export.Pdf ", "export.pdf", "basic" }; var card = Card(features: input);
        input[0] = "admin"; var exposed = card.Features; exposed[0] = "admin";
        Assert.Equal(new[] { "export.pdf", "basic" }, card.Features);
    }
    [Theory] [InlineData("")] [InlineData("1start")] [InlineData("has space")] [InlineData("中文")] [InlineData("a/b")]
    public void InvalidFeatureIdentifiersAreRejected(string feature)
        => Assert.Equal("VALIDATION_ERROR", Assert.Throws<RuleException>(() => Card(features: [feature])).Code);
    [Fact] public void FeatureCountIsLimitedAfterDeduplication()
    {
        Assert.Equal("VALIDATION_ERROR", Assert.Throws<RuleException>(() => Card(features: Enumerable.Range(0, 51).Select(x => "f" + x).ToArray())).Code);
        Assert.Single(Card(features: Enumerable.Repeat("same", 60).ToArray()).Features);
    }
}
