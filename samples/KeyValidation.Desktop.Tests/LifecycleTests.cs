using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class LifecycleTests
{
    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    [InlineData(0)]
    public async Task TransientHeartbeatFailureDoesNotExtendTheLastLease(int status)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));
        fixture.Handler.Response = (_, _) => status == 0 ? throw new HttpRequestException("raw-sensitive-network-detail") : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));
        Assert.False((await fixture.Coordinator.HeartbeatAsync()).Success);
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("LEASE_EXPIRED", fixture.Coordinator.PerformBasicAction().Code);
    }

    [Theory]
    [InlineData("LICENSE_DISABLED", 403)]
    [InlineData("LICENSE_FROZEN", 403)]
    [InlineData("LICENSE_EXPIRED", 403)]
    [InlineData("SESSION_EXPIRED", 403)]
    [InlineData("SESSION_REVOKED", 403)]
    [InlineData("CLIENT_UPGRADE_REQUIRED", 426)]
    public async Task DefinitiveHeartbeatRejectionImmediatelyRevokesBasicPermission(string code, int status)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = JsonContent.Create(new { code }) });
        Assert.Equal(code, (await fixture.Coordinator.HeartbeatAsync()).Code);
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Theory]
    [InlineData("clear")]
    [InlineData("mode")]
    [InlineData("close")]
    public async Task ActivationResponseAfterEndingItsContextCannotRestorePermission(string ending)
    {
        using var fixture = new CoordinatorFixture();
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = (_, _) => completion.Task;
        var pending = fixture.Coordinator.ActivateAsync("unit-test-card");
        EndContext(fixture.Coordinator, ending);
        completion.SetResult(fixture.Grant());
        await pending;
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task HeartbeatResponseAfterClearCannotRestorePermission()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        var completion = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = (_, _) => completion.Task;
        var pending = fixture.Coordinator.HeartbeatAsync();
        fixture.Coordinator.Clear();
        completion.SetResult(fixture.Grant(token: null));
        await pending;
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task AutomaticTicksAndManualHeartbeatShareOneInFlightRequest()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handler.Response = async (_, _) => { await release.Task; return fixture.Grant(token: null); };
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        fixture.Coordinator.SetAutomaticHeartbeat(true);
        var first = fixture.Coordinator.TickAsync();
        var second = fixture.Coordinator.TickAsync();
        var manual = fixture.Coordinator.HeartbeatAsync();
        var actualCalls = fixture.Handler.Calls;
        release.SetResult();
        await Task.WhenAll(first, second, manual);
        Assert.Equal(2, actualCalls); // Initial activation plus exactly one heartbeat.
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task PausingAutomaticHeartbeatsDoesNotPreventLeaseExpiry()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Coordinator.SetAutomaticHeartbeat(false);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        await fixture.Coordinator.TickAsync();
        Assert.Equal(1, fixture.Handler.Calls);
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task AutomaticHeartbeatWaitsForTheServerInterval()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        await fixture.Coordinator.TickAsync();
        Assert.Equal(1, fixture.Handler.Calls);
    }

    private static void EndContext(SessionCoordinator coordinator, string ending)
    {
        if (ending == "clear") coordinator.Clear();
        else if (ending == "mode") coordinator.Configure(new DemoProfile { Mode = AuthorizationMode.Offline, DeviceId = "unit-test-device" });
        else coordinator.Dispose();
    }
}
