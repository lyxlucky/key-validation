using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class ProfileTests
{
    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://user:password@example.com")]
    [InlineData("http://127.0.0.1:5080?card=private")]
    [InlineData("https://example.com/#fragment")]
    [InlineData("https://example.com/api")]
    [InlineData("file:///C:/private")]
    public void UnsafeOrMisleadingBaseUrlsAreRejectedBeforeAnyHttp(string address)
    {
        Assert.Throws<ProfileException>(() => DemoProfile.ValidateApiUrl(address));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://localhost:5080/")]
    [InlineData("http://[::1]:5080/")]
    [InlineData("https://example.com")]
    public void ExplicitLoopbackHttpAndRemoteHttpsAreAccepted(string address) => Assert.True(DemoProfile.ValidateApiUrl(address).IsAbsoluteUri);

    [Theory]
    [InlineData("01.2.3")]
    [InlineData("1.02.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3 ")]
    [InlineData("10000.0.0")]
    public void InvalidSuppliedClientVersionsCannotReachTheApi(string version) =>
        Assert.Throws<ProfileException>(() => new DemoProfile { DeviceId = "test", ClientVersion = version }.Validate());

    [Theory]
    [InlineData("")]
    [InlineData("0.0.0")]
    [InlineData("9999.9999.9999")]
    public void LegacyEmptyAndStrictBoundaryVersionsRemainSupported(string version) =>
        new DemoProfile { DeviceId = "test", ClientVersion = version }.Validate();
}
