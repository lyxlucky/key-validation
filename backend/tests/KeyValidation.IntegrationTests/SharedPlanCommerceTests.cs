using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanCommerceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SameChannelAndPlanHaveIndependentPricesPerApplication()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var (channel, _, accountId) = await CommerceTestData.Channel(fixture, admin, a.Id);
        var channelId = CommerceTestData.Id(channel);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(
            $"/api/admin/channels/{channelId}",
            new UpdateChannel("两应用渠道", accountId, [a.Id, b.Id], true))).StatusCode);

        await CommerceTestData.Price(admin, channelId, a.Id, plan.Id, 1100);
        await CommerceTestData.Price(admin, channelId, b.Id, plan.Id, 2200);
        await CommerceTestData.Price(admin, channelId, a.Id, plan.Id, 1300);
        var customerA = await CommerceTestData.Customer(admin, a.Id, channelId);
        var customerB = await CommerceTestData.Customer(admin, b.Id, channelId);
        var orderA = await CommerceTestData.Order(admin, a.Id, plan.Id, CommerceTestData.Id(customerA));
        var orderB = await CommerceTestData.Order(admin, b.Id, plan.Id, CommerceTestData.Id(customerB));
        Assert.Equal(1300, orderA.GetProperty("unitPriceCents").GetInt64());
        Assert.Equal(2200, orderB.GetProperty("unitPriceCents").GetInt64());

        var detail = (await admin.GetFromJsonAsync<ChannelDto>(
            $"/api/admin/channels/{channelId}"))!;
        Assert.Equal(2, detail.Prices.Count(x => x.PlanId == plan.Id));
        Assert.Contains(detail.Prices, x => x.ApplicationId == a.Id && x.UnitPriceCents == 1300);
        Assert.Contains(detail.Prices, x => x.ApplicationId == b.Id && x.UnitPriceCents == 2200);
    }

    [Fact]
    public async Task ExistingOrderFulfillsItsSnapshotAfterGlobalAndReferencePause()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, b.Id);
        var order = await CommerceTestData.Order(admin, b.Id, plan.Id, CommerceTestData.Id(customer));
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id, false);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(
            $"/api/admin/plans/{plan.Id}",
            new UpdatePlan("新规则", 3600, 2, false, 2, 5, ["new"]))).StatusCode);

        var fulfilled = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order));
        Assert.Equal("fulfilled", fulfilled.GetProperty("status").GetString());
        Assert.Equal(plan.DurationSeconds,
            fulfilled.GetProperty("snapshot").GetProperty("durationSeconds").GetInt64());
        Assert.Equal(plan.Name, fulfilled.GetProperty("snapshot").GetProperty("planName").GetString());
    }
}
