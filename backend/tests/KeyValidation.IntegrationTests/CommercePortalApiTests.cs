using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommercePortalApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CredentialPermissionReductionAndDisableTakeEffectImmediately()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var created = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Reduced", permissions = new[] { "licenses.read" } }), HttpStatusCode.Created);
        var id = created.GetProperty("credential").GetProperty("id").GetGuid(); using var api = fixture.CreateClient(); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/api-credentials/{id}", new { name = "Reduced", permissions = Array.Empty<string>(), isEnabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/v1/licenses")).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await api.GetAsync("/api/v1/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/api-credentials/{id}", new { name = "Reduced", permissions = new[] { "licenses.read" }, isEnabled = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/v1/licenses")).StatusCode);
    }

    [Fact]
    public async Task PortalBearerIsIndependentAndCannotReadAnotherCustomerOrOldSessionAfterRotation()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var own = await CommerceTestData.Customer(admin, app.Id); var other = await CommerceTestData.Customer(admin, app.Id);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(other)); var done = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order));
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/portal/me")).StatusCode);
        var (portal, code) = await CommerceTestData.Portal(fixture, admin, CommerceTestData.Id(own)); using var portalCleanup = portal;
        Assert.Equal(HttpStatusCode.OK, (await portal.GetAsync("/api/portal/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/admin/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/orders/{CommerceTestData.Id(order)}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/orders/{CommerceTestData.Id(order)}/delivery")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await portal.GetAsync($"/api/portal/licenses/{done.GetProperty("licenseIds")[0].GetGuid()}")).StatusCode);
        Assert.Equal(0, (await portal.GetFromJsonAsync<JsonElement>("/api/portal/orders")).GetProperty("total").GetInt32());
        await admin.PostAsync($"/api/admin/customers/{CommerceTestData.Id(own)}/rotate-access-code", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.PostAsJsonAsync("/api/portal/sessions", new { accessCode = code })).StatusCode);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("anonymize")]
    public async Task CustomerAccessChangesImmediatelyRevokePortalSessions(string action)
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var id = CommerceTestData.Id(customer);
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, id); using var portalCleanup = portal;
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/customers/{id}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
    }

    [Fact]
    public async Task PortalSessionExpiresAtEightHoursAndLogoutRevokesIt()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var id = CommerceTestData.Id(customer);
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, id); using var portalCleanup = portal;
        Assert.Equal(HttpStatusCode.NoContent, (await portal.DeleteAsync("/api/portal/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
        var (expiring, _) = await CommerceTestData.Portal(fixture, admin, id); using var expiryCleanup = expiring;
        fixture.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, (await expiring.GetAsync("/api/portal/me")).StatusCode);
    }

    [Fact]
    public async Task RenewalAndRebindingReviewsAreAtomicAndReplayOnlyOnce()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var id = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, id); var orderId = CommerceTestData.Id(order);
        var fulfilled = await CommerceTestData.Fulfill(admin, orderId); var licenseId = fulfilled.GetProperty("licenseIds")[0].GetGuid();
        var delivery = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/orders/{orderId}/delivery"); var key = delivery.GetProperty("licenses")[0].GetProperty("key").GetString()!;
        using var client = fixture.CreateClient(); var activation = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, key, "portal-device"))).Content.ReadFromJsonAsync<ActivationDto>())!;
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!; var bindingId = detail.Bindings[0].Id;
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, id); using var portalCleanup = portal;
        var renewal = await CommerceTestData.Json(await portal.PostAsJsonAsync("/api/portal/requests", new { requestId = Guid.NewGuid(), type = "renewal", licenseId }), HttpStatusCode.Created);
        var renewalId = CommerceTestData.Id(renewal);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/portal-requests/{renewalId}/review", new { status = "approved", reason = "manual renewal", planId = plan.Id, unitPriceCents = 3456 })).StatusCode);
        Assert.Equal(activation.ExpiresAt.AddDays(1), (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!.License.ExpiresAt);
        var rebinding = await CommerceTestData.Json(await portal.PostAsJsonAsync("/api/portal/requests", new { requestId = Guid.NewGuid(), type = "rebinding", licenseId, bindingId }), HttpStatusCode.Created);
        for (var i = 0; i < 2; i++) Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/portal-requests/{CommerceTestData.Id(rebinding)}/review", new { status = "approved", reason = "verified device replacement" })).StatusCode);
        var policy = (await admin.GetFromJsonAsync<DevicePolicyDto>($"/api/admin/licenses/{licenseId}/device-policy"))!; Assert.Equal(1, policy.UsedChanges);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", activation.SessionToken);
        Assert.NotEqual(HttpStatusCode.OK, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
    }

    [Fact]
    public async Task ApiCredentialHasIndependentScopeRotationPermissionsAndIdempotency()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var created = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Integration", permissions = new[] { "customers.manage", "orders.manage", "licenses.read" } }), HttpStatusCode.Created);
        var credentialId = created.GetProperty("credential").GetProperty("id").GetGuid(); var secret = created.GetProperty("secret").GetString()!;
        using var api = fixture.CreateClient(); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret);
        var requestId = Guid.NewGuid(); var payload = new { requestId, applicationId = app.Id, name = "External customer" };
        var customer = await CommerceTestData.Json(await api.PostAsJsonAsync("/api/v1/customers", payload), HttpStatusCode.Created);
        Assert.Equal(CommerceTestData.Id(customer), CommerceTestData.Id(await CommerceTestData.Json(await api.PostAsJsonAsync("/api/v1/customers", payload), HttpStatusCode.Created)));
        Assert.Equal(HttpStatusCode.Conflict, (await api.PostAsJsonAsync("/api/v1/customers", new { requestId, applicationId = app.Id, name = "Changed content" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.PostAsJsonAsync("/api/v1/customers", new { applicationId = app.Id, name = "Missing idempotency" })).StatusCode);
        var order = await CommerceTestData.Json(await api.PostAsJsonAsync("/api/v1/orders", new { requestId = Guid.NewGuid(), applicationId = app.Id, planId = plan.Id, customerId = CommerceTestData.Id(customer), type = "issue", quantity = 1, unitPriceCents = 100 }), HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.OK, (await api.PostAsJsonAsync($"/api/v1/orders/{CommerceTestData.Id(order)}/fulfill", new { requestId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Elevate", permissions = new[] { "platform.manage" } })).StatusCode);
        var rotated = await CommerceTestData.Json(await admin.PostAsync($"/api/admin/api-credentials/{credentialId}/rotate", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.GetAsync("/api/v1/licenses")).StatusCode);
        api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.GetProperty("secret").GetString());
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/licenses")).StatusCode);
        Assert.DoesNotContain(secret, await admin.GetStringAsync("/api/admin/api-credentials"));
    }

    [Fact]
    public async Task ApiCredentialLimiterUsesTheInjectedClockAndIsIndependentPerCredential()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var created = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Limited", permissions = new[] { "licenses.read" } }), HttpStatusCode.Created);
        using var api = fixture.CreateClient(); api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", created.GetProperty("secret").GetString());
        for (var i = 0; i < 60; i++) Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/licenses")).StatusCode);
        var rejected = await api.GetAsync("/api/v1/licenses"); Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode); Assert.NotNull(rejected.Headers.RetryAfter);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await api.GetAsync("/api/v1/licenses")).StatusCode);
    }
}
