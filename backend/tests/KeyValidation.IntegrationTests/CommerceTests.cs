using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

internal static class CommerceTestData
{
    internal static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    internal static Guid Id(JsonElement value) => value.GetProperty("id").GetGuid();
    internal static async Task<JsonElement> Customer(HttpClient client, Guid app, Guid? channel = null, string name = "Commerce customer") =>
        await Json(await client.PostAsJsonAsync("/api/admin/customers", new { applicationId = app, channelId = channel, name, email = "private@example.test", phone = "+86-private", note = "private-note", requestId = Guid.NewGuid() }), HttpStatusCode.Created);
    internal static async Task<JsonElement> Order(HttpClient client, Guid app, Guid plan, Guid customer, int quantity = 1, string type = "issue", Guid? license = null, Guid? requestId = null) =>
        await Json(await client.PostAsJsonAsync("/api/admin/orders", new { requestId = requestId ?? Guid.NewGuid(), applicationId = app, planId = plan, customerId = customer, quantity, type, unitPriceCents = 1234, licenseId = license }), HttpStatusCode.Created);
    internal static async Task<JsonElement> Fulfill(HttpClient client, Guid order, Guid? requestId = null) =>
        await Json(await client.PostAsJsonAsync($"/api/admin/orders/{order}/fulfill", new { requestId = requestId ?? Guid.NewGuid() }));
    internal static async Task<(HttpClient Client, string Code)> Portal(ApiFixture fixture, HttpClient admin, Guid customer)
    {
        var generated = await Json(await admin.PostAsync($"/api/admin/customers/{customer}/rotate-access-code", null));
        var code = generated.GetProperty("accessCode").GetString()!;
        var client = fixture.CreateClient();
        var session = await Json(await client.PostAsJsonAsync("/api/portal/sessions", new { accessCode = code }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        return (client, code);
    }
    internal static async Task<(JsonElement Channel, string UserName, string UserId)> Channel(ApiFixture fixture, HttpClient admin, Guid application, int quota = 10, string[]? permissions = null)
    {
        var role = await RbacTestData.Role(admin, permissions ?? ["customers.manage", "orders.manage", "licenses.read", "licenses.issue", "licenses.manage", "reports.read", "audit.read", "risk.review"]);
        var (id, name) = await RbacTestData.User(fixture);
        await RbacTestData.Assign(admin, id, [role.Id]);
        var channel = await Json(await admin.PostAsJsonAsync("/api/admin/channels", new { name = "Channel " + Guid.NewGuid().ToString("N"), accountId = id, applicationIds = new[] { application }, initialQuota = quota }), HttpStatusCode.Created);
        return (channel, name, id);
    }
    internal static async Task<JsonElement> Price(HttpClient admin, Guid channel, Guid app, Guid plan, long cents = 2345) =>
        await Json(await admin.PutAsJsonAsync($"/api/admin/channels/{channel}/prices", new { applicationId = app, planId = plan, unitPriceCents = cents }));
}

public sealed class CommerceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task PublicMoneyAndQuotaStayWithinExactJsonIntegerRange()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, customerId = CommerceTestData.Id(customer), planId = plan.Id, type = "issue", quantity = 100, unitPriceCents = 90_071_992_547_410L })).StatusCode);
        var (userId, _) = await RbacTestData.User(fixture);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/channels", new { name = "Unsafe quota", accountId = userId, applicationIds = new[] { app.Id }, initialQuota = 9_007_199_254_740_992L })).StatusCode);
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 1);
        var id = CommerceTestData.Id(channel);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/channels/{id}/quota-adjustments", new { requestId = Guid.NewGuid(), delta = 9_007_199_254_740_991L, reason = "Exceeds exact balance" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/channels/{id}/prices", new { applicationId = app.Id, planId = plan.Id, unitPriceCents = 90_071_992_547_410L })).StatusCode);
    }

    [Fact]
    public async Task CustomerCrudAndAnonymizationRetainFactsWithoutPrivateAuditData()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        var id = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, id);
        var updated = await CommerceTestData.Json(await admin.PatchAsJsonAsync($"/api/admin/customers/{id}", new { name = "Updated private person", email = "changed@example.test", phone = "private-phone", note = "private-note" }));
        Assert.Equal("Updated private person", updated.GetProperty("name").GetString());
        var anonymous = await CommerceTestData.Json(await admin.PostAsync($"/api/admin/customers/{id}/anonymize", null));
        foreach (var field in new[] { "name", "email", "phone", "note" }) Assert.Equal(JsonValueKind.Null, anonymous.GetProperty(field).ValueKind);
        Assert.Equal(id, CommerceTestData.Id(anonymous));
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/admin/orders/{CommerceTestData.Id(order)}")).StatusCode);
        var audit = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={id}");
        foreach (var value in new[] { "private@example.test", "changed@example.test", "private-phone", "private-note", "Updated private person" }) Assert.DoesNotContain(value, audit);
        Assert.Contains("customer", audit);
    }

    [Fact]
    public async Task FulfillmentUsesCreationSnapshotAndRecoversOriginalDeliveryAfterReplay()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        var requestId = Guid.NewGuid(); var id = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, id, 2, requestId: requestId);
        var orderId = CommerceTestData.Id(order);
        var replay = await CommerceTestData.Order(admin, app.Id, plan.Id, id, 2, requestId: requestId);
        Assert.Equal(orderId, CommerceTestData.Id(replay));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId, applicationId = app.Id, planId = plan.Id, customerId = id, quantity = 3, type = "issue", unitPriceCents = 1234 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Changed", 3600, 2, true, 2, 5, ["changed"]))).StatusCode);
        var fulfilled = await CommerceTestData.Fulfill(admin, orderId);
        Assert.Equal("fulfilled", fulfilled.GetProperty("status").GetString());
        Assert.Equal(86400, fulfilled.GetProperty("snapshot").GetProperty("durationSeconds").GetInt64());
        var delivery = await CommerceTestData.Json(await admin.GetAsync($"/api/admin/orders/{orderId}/delivery"));
        Assert.Equal(2, delivery.GetProperty("licenses").GetArrayLength());
        foreach (var key in delivery.GetProperty("licenses").EnumerateArray())
        {
            var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{CommerceTestData.Id(key)}"))!;
            Assert.Equal(86400, detail.License.DurationSeconds); Assert.Equal(1, detail.License.MaxDevices); Assert.Empty(detail.License.Features);
            Assert.DoesNotContain(key.GetProperty("key").GetString()!, fulfilled.GetRawText());
        }
        var afterReplay = await CommerceTestData.Fulfill(admin, orderId);
        Assert.Equal(fulfilled.GetProperty("licenseIds").GetRawText(), afterReplay.GetProperty("licenseIds").GetRawText());
        Assert.Equal(delivery.GetRawText(), (await CommerceTestData.Json(await admin.GetAsync($"/api/admin/orders/{orderId}/delivery"))).GetRawText());
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.OperationsEvents.CountAsync(x => x.ResourceId == orderId && x.EventType == "order.fulfilled"));
    }

    [Fact]
    public async Task ConcurrentFulfillmentCreatesOneBatchAndOneDurableEvent()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer), 3);
        var id = CommerceTestData.Id(order);
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => admin.PostAsJsonAsync($"/api/admin/orders/{id}/fulfill", new { requestId = Guid.NewGuid() })));
        foreach (var response in responses) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.Licenses.CountAsync(x => x.ApplicationId == app.Id));
        Assert.Equal(1, await db.Batches.CountAsync(x => x.ApplicationId == app.Id));
        Assert.Equal(1, await db.OperationsEvents.CountAsync(x => x.ResourceId == id && x.EventType == "order.fulfilled"));
    }

    [Fact]
    public async Task OrdersOnlyOperatorCanFulfillButCannotDirectlyIssue()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        var role = await RbacTestData.Role(admin, ["orders.manage"]); var (userId, name) = await RbacTestData.User(fixture);
        await RbacTestData.Assign(admin, userId, [role.Id]); using var actor = await fixture.Login(name);
        var order = await CommerceTestData.Order(actor, app.Id, plan.Id, CommerceTestData.Id(customer));
        Assert.Equal("fulfilled", (await CommerceTestData.Fulfill(actor, CommerceTestData.Id(order))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/admin/catalog")).StatusCode);
    }

    [Fact]
    public async Task CancellationAndRenewalEnforceStatusOwnershipAndIntegerHours()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var customerId = CommerceTestData.Id(customer);
        var pending = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId);
        var pendingId = CommerceTestData.Id(pending);
        Assert.Equal("cancelled", (await CommerceTestData.Json(await admin.PostAsync($"/api/admin/orders/{pendingId}/cancel", null))).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/orders/{pendingId}/fulfill", new { requestId = Guid.NewGuid() })).StatusCode);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId); var orderId = CommerceTestData.Id(order);
        var fulfilled = await CommerceTestData.Fulfill(admin, orderId); var licenseId = fulfilled.GetProperty("licenseIds")[0].GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/orders/{orderId}/cancel", null)).StatusCode);
        var renewal = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId, type: "renewal", license: licenseId);
        await CommerceTestData.Fulfill(admin, CommerceTestData.Id(renewal)); await CommerceTestData.Fulfill(admin, CommerceTestData.Id(renewal));
        Assert.Equal(172800, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!.License.DurationSeconds);
        var other = await CommerceTestData.Customer(admin, app.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, planId = plan.Id, customerId = CommerceTestData.Id(other), type = "renewal", quantity = 1, unitPriceCents = 1, licenseId })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, planId = plan.Id, customerId, type = "renewal", quantity = 1, unitPriceCents = 1, licenseId, additionalSeconds = 3601 })).StatusCode);
    }

    [Fact]
    public async Task DeliveryExpiresAtThirtyDaysButOrderAndLicenseFactsSurvive()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer)); var id = CommerceTestData.Id(order);
        await CommerceTestData.Fulfill(admin, id);
        fixture.Clock.Advance(TimeSpan.FromDays(30));
        using var current = await fixture.Login();
        Assert.Equal(HttpStatusCode.Gone, (await current.GetAsync($"/api/admin/orders/{id}/delivery")).StatusCode);
        var detail = await CommerceTestData.Json(await current.GetAsync($"/api/admin/orders/{id}"));
        Assert.False(detail.GetProperty("deliveryAvailable").GetBoolean()); Assert.Single(detail.GetProperty("licenseIds").EnumerateArray());
    }
}
