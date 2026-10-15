using System.Net;
using System.Net.Http.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class CoordinatorTests
{
    [Fact]
    public async Task ActivationUsesTheActualLeaseResponseBeforeGrantingBasicAction()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
        Assert.True((await fixture.Coordinator.ActivateAsync("unit-test-card")).Success);
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task BasicCalculationReturnsItsActualResultOnlyWhileAuthorizationIsValid()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        Assert.Null(fixture.Coordinator.PerformBasicAction().DemoValue);
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        Assert.Equal(42, fixture.Coordinator.PerformBasicAction().DemoValue);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        var expired = fixture.Coordinator.PerformBasicAction();
        Assert.False(expired.Success);
        Assert.Null(expired.DemoValue);
    }

    [Fact]
    public async Task ExactLeaseExpiryDeniesEvenWhenNoUiTimerHasRefreshed()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(10) - TimeSpan.FromTicks(1));
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
        fixture.Clock.Advance(TimeSpan.FromTicks(1));
        Assert.Equal("LEASE_EXPIRED", fixture.Coordinator.PerformBasicAction().Code);
    }

    [Fact]
    public async Task BasicGrantCannotExportAndDoesNotCreateAnOutputFile()
    {
        using var fixture = new CoordinatorFixture();
        using var directory = new TemporaryDirectory();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant(features: []));
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
        var output = Path.Combine(directory.Path, "denied.csv");
        Assert.Equal("FEATURE_NOT_GRANTED", fixture.Coordinator.ExportCsv(output).Code);
        Assert.False(File.Exists(output));
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task ExportRechecksAfterTheSavePathWasSelectedAndNeverOverwritesOnExpiry()
    {
        using var fixture = new CoordinatorFixture();
        using var directory = new TemporaryDirectory();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        Assert.True(fixture.Coordinator.PerformBasicAction().Success);
        var output = Path.Combine(directory.Path, "existing.csv");
        File.WriteAllText(output, "keep-me");
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("LEASE_EXPIRED", fixture.Coordinator.ExportCsv(output).Code);
        Assert.Equal("keep-me", File.ReadAllText(output));
    }

    [Fact]
    public async Task NetworkRoundTripConsumesRatherThanExtendsTheGrantedLease()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) =>
        {
            var response = fixture.Grant();
            fixture.Clock.Advance(TimeSpan.FromSeconds(4));
            return Task.FromResult(response);
        };
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(fixture.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task NamedFeatureGrantWritesTheActualDemoCsv()
    {
        using var fixture = new CoordinatorFixture();
        using var directory = new TemporaryDirectory();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("unit-test-card");
        var output = Path.Combine(directory.Path, "granted.csv");
        Assert.True(fixture.Coordinator.ExportCsv(output).Success);
        Assert.Equal("item,quantity\n演示项目,1\n", File.ReadAllText(output));
    }
}

internal sealed class CoordinatorFixture : IDisposable
{
    public TestClock Clock { get; } = new();
    public MemoryTimeStore TimeStore { get; } = new();
    public ControlledHandler Handler { get; } = new();
    public SessionCoordinator Coordinator { get; }
    public int ClientCreations { get; private set; }
    public DemoProfile Profile { get; } = new() { AppCode = "desktop-test", DeviceId = "unit-test-device", ClientVersion = "2.1.0" };
    public CoordinatorFixture()
    {
        Coordinator = new(_ => { ClientCreations++; return new HttpClient(Handler, disposeHandler: false) { BaseAddress = new Uri("http://127.0.0.1:5080") }; }, TimeStore, Clock);
        Coordinator.Configure(Profile);
    }
    public HttpResponseMessage Grant(string[]? features = null, int leaseSeconds = 10, string? token = "unit-test-token") =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(new LeaseGrant(Clock.GetUtcNow(), Clock.GetUtcNow().AddDays(1),
            Clock.GetUtcNow().AddSeconds(leaseSeconds), Clock.GetUtcNow().AddHours(1), 2, token,
            Guid.Parse("11111111-1111-1111-1111-111111111111"), false, features ?? ["export"])) };
    public void Dispose() { Coordinator.Dispose(); Handler.Dispose(); }
}

internal sealed class ControlledHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Response { get; set; } = (_, _) => throw new InvalidOperationException("Unexpected HTTP request in test.");
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Calls);
        return Response(request, cancellationToken);
    }
}

internal sealed class TestClock : TimeProvider
{
    public DateTimeOffset Utc { get; set; } = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => Utc;
    public override long GetTimestamp() => ticks;
    public void Advance(TimeSpan duration) { ticks += duration.Ticks; Utc += duration; }
}

internal sealed class MemoryTimeStore : ITrustedTimeStore
{
    public DateTimeOffset? Latest { get; private set; }
    public bool Unavailable { get; set; }
    public DateTimeOffset? ReadLatest() => Unavailable ? throw new IOException() : Latest;
    public DateTimeOffset SaveLatest(DateTimeOffset value)
    {
        if (Unavailable) throw new IOException();
        if (Latest is null || value > Latest) Latest = value;
        return Latest.Value;
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "keyvalidation-desktop-tests-" + Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose() => Directory.Delete(Path, recursive: true);
}
