using System.Net;
using System.Net.Http.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class ExtensionTests
{
    [Theory]
    [InlineData(204, true, "LOGOUT_CONFIRMED")]
    [InlineData(503, false, "LOGOUT_UNCONFIRMED")]
    [InlineData(200, false, "LOGOUT_UNCONFIRMED")]
    public async Task LogoutReportsRemoteSuccessOnlyForConfirmedNoContent(int status, bool success, string code)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Handler.Response = (request, _) =>
        {
            Assert.Equal("/api/client/sessions/logout", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("unit-test-token", request.Headers.Authorization?.Parameter);
            Assert.Null(request.Content);
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        };
        var result = await fixture.Coordinator.LogoutAsync();
        Assert.Equal(success, result.Success);
        Assert.Equal(code, result.Code);
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task LogoutEndsLocalPermissionBeforeTheRemoteRequestCompletes()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = (_, _) => completion.Task;
        var pending = fixture.Coordinator.LogoutAsync();
        var wasAuthorized = fixture.Coordinator.PerformBasicAction().Success;
        completion.SetResult(new(HttpStatusCode.NoContent));
        await pending;
        Assert.False(wasAuthorized);
    }

    [Theory]
    [InlineData("REBIND_COOLDOWN")]
    [InlineData("REBIND_LIMIT_REACHED")]
    public async Task DeviceReleaseRejectionIsNeverReportedAsSuccessful(string code)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Handler.Response = (request, _) =>
        {
            Assert.Equal("/api/client/device-bindings/release", request.RequestUri!.AbsolutePath);
            Assert.Equal("unit-test-token", request.Headers.Authorization?.Parameter);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { code }) });
        };
        var result = await fixture.Coordinator.ReleaseDeviceAsync();
        Assert.False(result.Success);
        Assert.Equal(code, result.Code);
    }

    [Fact]
    public async Task UncertainDeviceReleaseCannotRetainLocalPermissions()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Handler.Response = (_, _) => throw new HttpRequestException("private network detail");
        Assert.Equal("RELEASE_UNCONFIRMED", (await fixture.Coordinator.ReleaseDeviceAsync()).Code);
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task PublicVersionUsesTheDocumentedAnonymousEndpointAndStoresGuidance()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/client/applications/desktop-test/version", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new
            {
                applicationCode = "desktop-test", minimumVersion = "2.1.0", latestVersion = "2.10.0", enforceMinimum = true,
                updateUrl = "https://example.com/update", releaseNotes = "测试升级说明", sha256 = (string?)null
            }) });
        };
        Assert.True((await fixture.Coordinator.CheckVersionAsync()).Success);
        var state = fixture.Coordinator.Snapshot();
        Assert.Equal("2.10.0", state.Version?.LatestVersion);
        Assert.True(state.Version?.UpgradeAvailable);
        Assert.False(state.Authorized);
    }

    [Fact]
    public async Task TrialActivationUsesItsOwnEndpointAndKeepsTheTrialFeatureSnapshot()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (request, _) => Task.FromResult(request.RequestUri!.AbsolutePath == "/api/client/trials/activate"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new LeaseGrant(fixture.Clock.Utc, fixture.Clock.Utc.AddHours(1),
                fixture.Clock.Utc.AddMinutes(3), fixture.Clock.Utc.AddHours(1), 60, "trial-test-token", Guid.NewGuid(), true, [])) }
            : new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code = "LICENSE_INVALID" }) });
        Assert.True((await fixture.Coordinator.ActivateTrialAsync()).Success);
        Assert.True(fixture.Coordinator.Snapshot().IsTrial);
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
        Assert.False(fixture.Coordinator.Snapshot().CanExport);
    }

    [Fact]
    public async Task PublicVersionAndExitButtonsCannotCreateHttpInOfflineMode()
    {
        using var fixture = new OfflineFixture();
        await fixture.Validate();
        await fixture.Online.Coordinator.CheckVersionAsync();
        await fixture.Online.Coordinator.ReleaseDeviceAsync();
        await fixture.Online.Coordinator.LogoutAsync();
        Assert.Equal(0, fixture.Online.ClientCreations);
        Assert.Equal(0, fixture.Online.Handler.Calls);
    }
}
