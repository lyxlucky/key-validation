using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommerceChannelTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ChannelAccountSeesOnlyApplicationAndChannelIntersectionIncludingLegacyRoutes()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (a, name, _) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var (b, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var aId = CommerceTestData.Id(a); var bId = CommerceTestData.Id(b);
        await CommerceTestData.Price(admin, aId, app.Id, plan.Id); await CommerceTestData.Price(admin, bId, app.Id, plan.Id);
        var own = await CommerceTestData.Customer(admin, app.Id, aId);
        var hidden = await CommerceTestData.Customer(admin, app.Id, bId);
        var legacy = await CommerceTestData.Customer(admin, app.Id);
        var ownOrder = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(own));
        var otherOrder = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(hidden));
        await CommerceTestData.Fulfill(admin, CommerceTestData.Id(ownOrder));
        var otherDone = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(otherOrder));
        await ApiFixture.Issue(admin, app, plan);
        using var actor = await fixture.Login(name);
        var customers = await actor.GetFromJsonAsync<JsonElement>("/api/admin/customers?pageSize=1"); Assert.Equal(1, customers.GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/admin/customers/{CommerceTestData.Id(hidden)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/admin/customers/{CommerceTestData.Id(legacy)}")).StatusCode);
        Assert.Equal(0, (await actor.GetFromJsonAsync<JsonElement>($"/api/admin/customers?channelId={bId}")).GetProperty("total").GetInt32());
        Assert.Equal(1, (await actor.GetFromJsonAsync<PagedResult<LicenseDto>>("/api/admin/licenses"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/admin/licenses/{otherDone.GetProperty("licenseIds")[0].GetGuid()}")).StatusCode);
        Assert.Equal(1, (await actor.GetFromJsonAsync<JsonElement>("/api/admin/license-batches")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(Guid.NewGuid(), [otherDone.GetProperty("licenseIds")[0].GetGuid()], "extend", 3600, Reason: "quota bypass"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync($"/api/admin/channels/{aId}/quota-adjustments", new { requestId = Guid.NewGuid(), delta = 100, reason = "self recharge" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.PostAsJsonAsync("/api/admin/customers", new { applicationId = app.Id, channelId = bId, name = "wrong", requestId = Guid.NewGuid() })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentQuotaExhaustionCannotOverspendAndPriceIsSnapshotted()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 1); var channelId = CommerceTestData.Id(channel);
        await CommerceTestData.Price(admin, channelId, app.Id, plan.Id, 5555);
        var customer = await CommerceTestData.Customer(admin, app.Id, channelId);
        var first = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer));
        var second = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer));
        await CommerceTestData.Price(admin, channelId, app.Id, plan.Id, 9999);
        var responses = await Task.WhenAll(new[] { first, second }.Select(order => admin.PostAsJsonAsync($"/api/admin/orders/{CommerceTestData.Id(order)}/fulfill", new { requestId = Guid.NewGuid() })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var done = await responses.Single(x => x.StatusCode == HttpStatusCode.OK).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(5555, done.GetProperty("unitPriceCents").GetInt64());
        var current = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/channels/{channelId}"); Assert.Equal(0, current.GetProperty("remainingQuota").GetInt64());
        var ledger = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/channels/{channelId}/quota-ledger");
        Assert.Single(ledger.GetProperty("items").EnumerateArray(), x => x.GetProperty("kind").GetString() == "fulfillment");
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); Assert.Equal(1, await db.Licenses.CountAsync(x => x.ApplicationId == app.Id));
    }

    [Fact]
    public async Task DisabledChannelRejectsNewBusinessButExistingClientLicenseStillActivates()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, name, accountId) = await CommerceTestData.Channel(fixture, admin, app.Id); var id = CommerceTestData.Id(channel);
        await CommerceTestData.Price(admin, id, app.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, app.Id, id);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer)); var orderId = CommerceTestData.Id(order);
        await CommerceTestData.Fulfill(admin, orderId);
        var delivery = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/orders/{orderId}/delivery"); var key = delivery.GetProperty("licenses")[0].GetProperty("key").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/channels/{id}", new { name = "Disabled channel", accountId, applicationIds = new[] { app.Id }, isEnabled = false })).StatusCode);
        using var actor = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/admin/customers", new { applicationId = app.Id, name = "blocked", requestId = Guid.NewGuid() })).StatusCode);
        using var client = fixture.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, key, "still-valid"))).StatusCode);
    }

    [Fact]
    public async Task QuotaAdjustmentsRequireReasonAndReplayWithoutDuplicatingTheLedger()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 0); var id = CommerceTestData.Id(channel); var requestId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/channels/{id}/quota-adjustments", new { requestId, delta = 3, reason = "" })).StatusCode);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/channels/{id}/quota-adjustments", new { requestId, delta = 3, reason = "manual allocation" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/channels/{id}/quota-adjustments", new { requestId, delta = 4, reason = "manual allocation" })).StatusCode);
        Assert.Equal(3, (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/channels/{id}")).GetProperty("remainingQuota").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/channels/{id}/quota-adjustments", new { requestId = Guid.NewGuid(), delta = -4, reason = "would overdraw" })).StatusCode);
    }
}
