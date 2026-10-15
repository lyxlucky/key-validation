using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class TransportTests
{
    [Fact]
    public void ClientTransportCannotForwardAuthorizationThroughRedirectsOrRetainCookies()
    {
        using var handler = DesktopTransport.CreateHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }

    [Fact]
    public async Task ExplicitProfileLoadKeepsCardInMemoryWithoutNetworkOrAutomaticActivation()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profile.json");
        const string json = "{\"schemaVersion\":1,\"mode\":\"online\",\"apiUrl\":\"http://127.0.0.1:5080\",\"appCode\":\"desktop-test\",\"deviceId\":\"unit-test-device\",\"licenseKey\":\"in-memory-test-card\"}";
        File.WriteAllText(path, json);
        var profile = await DemoProfile.LoadAsync(path);
        using var fixture = new CoordinatorFixture();
        fixture.Coordinator.Configure(profile);
        Assert.Equal("in-memory-test-card", profile.LicenseKey);
        Assert.False(fixture.Coordinator.Snapshot().Authorized);
        Assert.Equal(0, fixture.ClientCreations);
        Assert.Equal(json, File.ReadAllText(path));
    }

    [Fact]
    public async Task ProfileCannotSmuggleDuplicateFieldsOrAUrlCredential()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "bad-profile.json");
        File.WriteAllText(path, "{\"schemaVersion\":1,\"mode\":\"online\",\"mode\":\"offline\",\"apiUrl\":\"https://user:password@example.com\",\"appCode\":\"desktop-test\",\"deviceId\":\"test\"}");
        var error = await Assert.ThrowsAsync<ProfileException>(() => DemoProfile.LoadAsync(path));
        Assert.Equal("PROFILE_INVALID", error.Code);
        Assert.DoesNotContain("password", error.Message);
    }
}
