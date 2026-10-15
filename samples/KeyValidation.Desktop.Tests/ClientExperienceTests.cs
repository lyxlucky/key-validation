using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class ClientExperienceTests
{
    [Fact]
    public async Task ActualLicenseDeadlineShowsExpiryInsteadOfANetworkTimeout()
    {
        using var fixture = new CoordinatorFixture();
        var now = fixture.Clock.GetUtcNow();
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new KeyValidation.Client.LeaseGrant(now, now.AddSeconds(3), now.AddSeconds(180), now.AddHours(1), 60, "unit-test-token"))
        });
        await fixture.Coordinator.ActivateAsync("test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(3));
        var status = ClientStatus.From(fixture.Coordinator.Snapshot());
        Assert.False(status.IsLoggedIn);
        Assert.Equal("LICENSE_EXPIRED", status.Code);
        Assert.Equal("卡密已到期", status.Title);
    }
    [Theory]
    [InlineData("LICENSE_DISABLED", "卡密已禁用")]
    [InlineData("LICENSE_FROZEN", "卡密已冻结")]
    [InlineData("LICENSE_EXPIRED", "卡密已到期")]
    [InlineData("SESSION_REVOKED", "登录已失效")]
    [InlineData("APPLICATION_DISABLED", "应用已停用")]
    public async Task ServerRejectionReturnsToLoginWithTheActualReason(string code, string title)
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("test-card");
        Assert.True(ClientStatus.From(fixture.Coordinator.Snapshot()).IsLoggedIn);
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = JsonContent.Create(new { code }) });
        await fixture.Coordinator.HeartbeatAsync();
        var status = ClientStatus.From(fixture.Coordinator.Snapshot());
        Assert.False(status.IsLoggedIn);
        Assert.Equal(title, status.Title);
        Assert.Equal(code, status.Code);
    }

    [Fact]
    public async Task ConnectionFailureKeepsOnlyTheExistingLeaseThenReturnsToLogin()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("test-card");
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        fixture.Handler.Response = (_, _) => throw new HttpRequestException();
        await fixture.Coordinator.HeartbeatAsync();
        var warning = ClientStatus.From(fixture.Coordinator.Snapshot());
        Assert.True(warning.IsLoggedIn);
        Assert.True(warning.IsWarning);
        fixture.Clock.Advance(TimeSpan.FromSeconds(8));
        var expired = ClientStatus.From(fixture.Coordinator.Snapshot());
        Assert.False(expired.IsLoggedIn);
        Assert.Equal("LEASE_EXPIRED", expired.Code);
    }

    [Fact]
    public async Task LastVerifiedTimeOnlyChangesOnAnAcceptedGrant()
    {
        using var fixture = new CoordinatorFixture();
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.ActivateAsync("test-card");
        var first = fixture.Coordinator.Snapshot().LastVerifiedAt;
        Assert.Equal(fixture.Clock.GetUtcNow(), first);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        fixture.Handler.Response = (_, _) => throw new HttpRequestException();
        await fixture.Coordinator.HeartbeatAsync();
        Assert.Equal(first, fixture.Coordinator.Snapshot().LastVerifiedAt);
        fixture.Handler.Response = (_, _) => Task.FromResult(fixture.Grant());
        await fixture.Coordinator.HeartbeatAsync();
        Assert.Equal(fixture.Clock.GetUtcNow(), fixture.Coordinator.Snapshot().LastVerifiedAt);
        fixture.Coordinator.Clear();
        Assert.Null(fixture.Coordinator.Snapshot().LastVerifiedAt);
    }
}
