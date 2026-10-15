using KeyValidation.Api;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class TotpTests
{
    // RFC 6238 Appendix B, SHA-1 vectors reduced to the specified six digits.
    private const string RfcSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void AcceptsPublishedRfcVectorsAndRejectsReplay(long seconds, string code)
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(seconds);
        Assert.True(Totp.Verify(RfcSecret, code, now, null, out var step));
        Assert.Equal(seconds / 30, step);
        Assert.False(Totp.Verify(RfcSecret, code, now, step, out _));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void AllowsOnlyAdjacentTimeSteps(int currentStep, bool accepted) =>
        Assert.Equal(accepted, Totp.Verify(RfcSecret, "287082", DateTimeOffset.FromUnixTimeSeconds(currentStep * 30), null, out _));
}
