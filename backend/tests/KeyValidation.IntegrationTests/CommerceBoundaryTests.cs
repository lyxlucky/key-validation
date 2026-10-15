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

public sealed class CommerceBoundaryTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OrdersOnlyCatalogProvidesNamesAndEligibleLicensesWithoutContactFields()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var customerId = CommerceTestData.Id(customer);
        var requestId = Guid.NewGuid(); var order = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId, requestId: requestId);
        var fulfilled = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order));
        var role = await RbacTestData.Role(admin, ["orders.manage"]); var (user, name) = await RbacTestData.User(fixture); await RbacTestData.Assign(admin, user, [role.Id]); using var actor = await fixture.Login(name);
        var catalog = await CommerceTestData.Json(await actor.GetAsync($"/api/admin/orders/catalog?customerId={customerId}"));
        Assert.Equal("Commerce customer", Assert.Single(catalog.GetProperty("customers").EnumerateArray(), x => CommerceTestData.Id(x) == customerId).GetProperty("name").GetString());
        Assert.Equal(fulfilled.GetProperty("licenseIds")[0].GetGuid(), CommerceTestData.Id(Assert.Single(catalog.GetProperty("licenses").EnumerateArray())));
        foreach (var field in new[] { "email", "phone", "note" }) Assert.DoesNotContain("\"" + field + "\"", catalog.GetRawText());
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/customers")).StatusCode);
        var recovery = await actor.GetFromJsonAsync<JsonElement>($"/api/admin/orders?requestId={requestId}"); Assert.Equal(1, recovery.GetProperty("total").GetInt32());
        Assert.Equal(CommerceTestData.Id(order), CommerceTestData.Id(recovery.GetProperty("items")[0]));
    }

    [Fact]
    public async Task ChannelApiCredentialEnforcesBothScopesAndFulfillsWithOrdersPermissionAlone()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, hiddenApp, hiddenPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 2); var channelId = CommerceTestData.Id(channel); await CommerceTestData.Price(admin, channelId, app.Id, plan.Id);
        var own = await CommerceTestData.Customer(admin, app.Id, channelId); var legacy = await CommerceTestData.Customer(admin, app.Id); var hidden = await CommerceTestData.Customer(admin, hiddenApp.Id);
        var secret = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, channelId, name = "Orders only channel API", permissions = new[] { "orders.manage" } }), HttpStatusCode.Created);
        using var api = fixture.CreateClient(); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret.GetProperty("secret").GetString());
        var catalog = await api.GetFromJsonAsync<JsonElement>("/api/v1/orders/catalog"); Assert.Single(catalog.GetProperty("customers").EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/v1/licenses")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/v1/customers")).StatusCode);
        foreach (var customer in new[] { legacy, hidden })
            Assert.Equal(HttpStatusCode.NotFound, (await api.PostAsJsonAsync("/api/v1/orders", new { requestId = Guid.NewGuid(), applicationId = customer.GetProperty("applicationId").GetGuid(), planId = customer.Equals(hidden) ? hiddenPlan.Id : plan.Id, customerId = CommerceTestData.Id(customer), type = "issue", quantity = 1, unitPriceCents = 1 })).StatusCode);
        var order = await CommerceTestData.Json(await api.PostAsJsonAsync("/api/v1/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, planId = plan.Id, customerId = CommerceTestData.Id(own), type = "issue", quantity = 1, unitPriceCents = 0 }), HttpStatusCode.Created);
        var id = CommerceTestData.Id(order); Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync($"/api/v1/orders/{id}/fulfill", new { requestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync($"/api/v1/orders/{id}/delivery")).StatusCode);
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/channels/{channelId}")).GetProperty("remainingQuota").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync("/api/v1/accounts")).StatusCode);
    }

    [Fact]
    public async Task FrozenRenewalRollsBackAndSnapshotRetainsAllAuthorizationConditions()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Snapshot plan", 172800, 3, true, 2, 7, ["export", "sync"]))).StatusCode);
        var customer = await CommerceTestData.Customer(admin, app.Id); var customerId = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId); var id = CommerceTestData.Id(order);
        await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("New values", 3600, 1, false, 1, null, []));
        var done = await CommerceTestData.Fulfill(admin, id); var licenseId = done.GetProperty("licenseIds")[0].GetGuid();
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!;
        Assert.Equal(172800, detail.License.DurationSeconds); Assert.Equal(3, detail.License.MaxDevices); Assert.Equal(2, detail.License.MaxConcurrentSessions); Assert.Equal(new[] { "export", "sync" }, detail.License.Features); Assert.Equal(detail.License.CreatedAt.AddDays(7), detail.License.ActivateBefore);
        await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Renewal", 3600, 1, true));
        var renewal = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId, type: "renewal", license: licenseId); var renewalId = CommerceTestData.Id(renewal);
        var delivery = (await admin.GetFromJsonAsync<OrderDeliveryDto>($"/api/admin/orders/{id}/delivery"))!;
        using var client = fixture.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, delivery.Licenses[0].Key, "freeze-test"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{licenseId}/freeze", new { reason = "review boundary" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync($"/api/admin/orders/{renewalId}/fulfill", new { requestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal("pending", (await admin.GetFromJsonAsync<OrderDto>($"/api/admin/orders/{renewalId}"))!.Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, customerId, planId = plan.Id, type = "renewal", quantity = 1, licenseId })).StatusCode);
    }

    [Fact]
    public async Task PortalRebindingCooldownRejectionLeavesRequestBindingAndQuotaUnchanged()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var customerId = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId); var orderId = CommerceTestData.Id(order); var done = await CommerceTestData.Fulfill(admin, orderId); var licenseId = done.GetProperty("licenseIds")[0].GetGuid();
        var delivery = (await admin.GetFromJsonAsync<OrderDeliveryDto>($"/api/admin/orders/{orderId}/delivery"))!;
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, customerId); using var portalCleanup = portal;
        using var client = fixture.CreateClient();
        Guid latestRequest = Guid.Empty; Guid latestBinding = Guid.Empty;
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, delivery.Licenses[0].Key, "replacement-" + i))).StatusCode);
            var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!; latestBinding = Assert.Single(detail.Bindings, b => b.ReleasedAt == null).Id;
            var request = await CommerceTestData.Json(await portal.PostAsJsonAsync("/api/portal/requests", new { requestId = Guid.NewGuid(), type = "rebinding", licenseId, bindingId = latestBinding }), HttpStatusCode.Created); latestRequest = CommerceTestData.Id(request);
            var review = await admin.PostAsJsonAsync($"/api/admin/portal-requests/{latestRequest}/review", new { status = "approved", reason = "verified owner" });
            Assert.Equal(i == 0 ? HttpStatusCode.OK : HttpStatusCode.Conflict, review.StatusCode);
        }
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("pending", (await db.PortalRequests.SingleAsync(x => x.Id == latestRequest)).Status); Assert.Null((await db.Bindings.SingleAsync(x => x.Id == latestBinding)).ReleasedAt);
        Assert.Equal(1, await db.RebindingEvents.CountAsync(x => x.LicenseId == licenseId));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/portal-requests/{latestRequest}/review", new { status = "rejected", reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/portal-requests/{latestRequest}/review", new { status = "rejected", reason = "cooldown applies" })).StatusCode);
    }

    [Fact]
    public async Task AdministratorCanFilterLegacyBusinessListsAndReportsByChannel()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id); var channelId = CommerceTestData.Id(channel); await CommerceTestData.Price(admin, channelId, app.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, app.Id, channelId); var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer)); await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order));
        await ApiFixture.Issue(admin, app, plan);
        foreach (var path in new[] { "licenses", "license-batches", "batch-overview" })
            Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/{path}?applicationId={app.Id}&channelId={channelId}")).GetProperty("total").GetInt32());
        var query = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/licenses/query", new { applicationId = app.Id, channelId })); Assert.Equal(1, query.GetProperty("total").GetInt32());
        var report = await admin.GetFromJsonAsync<OperationsReport>($"/api/admin/reports/operations?applicationId={app.Id}&channelId={channelId}"); Assert.Equal(1, report!.Totals.Issued);
        var licenseId = (await admin.GetFromJsonAsync<OrderDto>($"/api/admin/orders/{CommerceTestData.Id(order)}"))!.LicenseIds[0];
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(Guid.NewGuid(), [licenseId], "extend", 3600, Reason: "must charge quota"))).StatusCode);
    }

    [Fact]
    public async Task ReassignedApplicationScopeAndDisabledChannelRevokePortalAndCredentialAccess()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, otherApp, _) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var (channel, name, accountId) = await CommerceTestData.Channel(fixture, admin, app.Id); var channelId = CommerceTestData.Id(channel);
        var customer = await CommerceTestData.Customer(admin, app.Id, channelId); var (portal, _) = await CommerceTestData.Portal(fixture, admin, CommerceTestData.Id(customer)); using var portalCleanup = portal;
        var created = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, channelId, name = "Bound channel", permissions = new[] { "licenses.read" } }), HttpStatusCode.Created);
        using var api = fixture.CreateClient(); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{accountId}/access", new { allApplications = false, applicationIds = new[] { otherApp.Id } })).StatusCode);
        using var actor = await fixture.Login(name); var catalog = await actor.GetFromJsonAsync<ReportsCatalogDto>("/api/admin/catalog"); Assert.Empty(catalog!.Applications); Assert.Empty(catalog.Plans);
        Assert.Equal(0, (await actor.GetFromJsonAsync<JsonElement>("/api/admin/customers")).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await actor.PostAsJsonAsync("/api/admin/customers", new { applicationId = app.Id, name = "out of application scope" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/channels/{channelId}", new { name = "disabled", accountId, applicationIds = new[] { app.Id }, isEnabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/v1/licenses")).StatusCode); Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
    }

    [Fact]
    public async Task FulfillmentKeyCannotBeReusedForAnotherOrderAndCustomerBoundsAreEnforced()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        foreach (var payload in new object[] { new { applicationId = app.Id, name = "" }, new { applicationId = app.Id, name = new string('x', 121) }, new { applicationId = app.Id, name = "name", email = new string('x', 255) }, new { applicationId = app.Id, name = "name", phone = new string('x', 41) }, new { applicationId = app.Id, name = "name", note = new string('x', 501) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/customers", payload)).StatusCode);
        var customer = await CommerceTestData.Customer(admin, app.Id); var customerId = CommerceTestData.Id(customer);
        foreach (var quantity in new[] { 0, 101 }) Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, customerId, planId = plan.Id, type = "issue", quantity })).StatusCode);
        var first = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId); var second = await CommerceTestData.Order(admin, app.Id, plan.Id, customerId); var requestId = Guid.NewGuid();
        await CommerceTestData.Fulfill(admin, CommerceTestData.Id(first), requestId);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/admin/orders/{CommerceTestData.Id(second)}/fulfill", new { requestId })).StatusCode);
        Assert.Equal("pending", (await admin.GetFromJsonAsync<OrderDto>($"/api/admin/orders/{CommerceTestData.Id(second)}"))!.Status);
    }
}
