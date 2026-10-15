using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityRiskTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("risk.review")]
    [InlineData("customers.manage")]
    [InlineData("orders.manage")]
    public async Task MinimalCatalogIsVisibleWithOnlyTheRequiredApplicationPermission(string permission)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (other, _, _) = await fixture.Catalog(); using var otherCleanup = other;
        var (id, name) = await User([app.Id], [permission]);
        using var staff = await fixture.Login(name);
        var response = await staff.GetAsync("/api/admin/catalog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = (await response.Content.ReadFromJsonAsync<ReportsCatalogDto>())!;
        Assert.Equal(app.Id, Assert.Single(catalog.Applications).Id);
        Assert.Equal(plan.Id, Assert.Single(catalog.Plans).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/reports/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new UpdateAccountAccess([], false, [app.Id]))).StatusCode);
        using var empty = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await empty.GetAsync("/api/admin/catalog")).StatusCode);
    }

    [Fact]
    public async Task RiskScanDeduplicatesSuccessfulActivationEvidenceAndReviewIsScoped()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var policyResponse = await admin.GetAsync($"/api/admin/applications/{app.Id}/risk-policy");
        Assert.Equal(HttpStatusCode.OK, policyResponse.StatusCode);
        var policy = (await policyResponse.Content.ReadFromJsonAsync<RiskPolicyDto>())!;
        Assert.Equal(10, policy.ActivationThreshold); Assert.Equal(10, policy.ActivationWindowMinutes);
        Assert.Equal(3, policy.RebindingThreshold); Assert.Equal(60, policy.RebindingWindowMinutes); Assert.Equal(24, policy.RegionWindowHours);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true, 0))).StatusCode);
        var issued = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "risk-device"))).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        using var secondAdmin = await fixture.Login();
        var scans = await Task.WhenAll(admin.PostAsync("/api/admin/operations/scan", null), secondAdmin.PostAsync("/api/admin/operations/scan", null));
        Assert.All(scans, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var risks = (await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!;
        var risk = Assert.Single(risks.Items); Assert.Equal(1, risks.Total);
        Assert.Equal("activation_burst", risk.Rule); Assert.Equal("open", risk.Status);
        Assert.Equal(10, JsonDocument.Parse(risk.EvidenceJson).RootElement.GetProperty("count").GetInt32());
        Assert.DoesNotContain("risk-device", risk.EvidenceJson);
        var (_, name) = await User([app.Id], ["risk.review"]); using var reviewer = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.PostAsJsonAsync($"/api/admin/risk-events/{risk.Id}/review", new ReviewRiskEvent("confirmed", " "))).StatusCode);
        var reviewed = await reviewer.PostAsJsonAsync($"/api/admin/risk-events/{risk.Id}/review", new ReviewRiskEvent("dismissed", "Known reconnect test"));
        Assert.Equal(HttpStatusCode.OK, reviewed.StatusCode);
        Assert.Equal("dismissed", (await reviewed.Content.ReadFromJsonAsync<RiskEventDto>())!.Status);
        var (_, hiddenName) = await User([], ["risk.review"]); using var hidden = await fixture.Login(hiddenName);
        Assert.Equal(0, (await hidden.GetFromJsonAsync<PagedResult<RiskEventDto>>("/api/admin/risk-events"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await hidden.GetAsync($"/api/admin/risk-events/{risk.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await hidden.PostAsJsonAsync($"/api/admin/risk-events/{risk.Id}/review", new ReviewRiskEvent("confirmed", "Cannot see"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reviewer.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(false))).StatusCode);
    }

    [Fact]
    public async Task RebindingRiskUsesSuccessfulReleasesAndDisabledPolicySuppressesNewEvents()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var response = await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var issued = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            if (i == 0) Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/licenses/{issued.Licenses[0].Id}/device-policy", new UpdateDevicePolicy(false, 3, 30, 0, "Risk fixture permits repeated release"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "release-device"))).StatusCode);
            var details = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{issued.Licenses[0].Id}"))!;
            var binding = details.Bindings.Single(x => x.ReleasedAt is null);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{binding.Id}/release", null)).StatusCode);
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var risk = Assert.Single((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items);
        Assert.Equal("rebinding_burst", risk.Rule);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(false, 1))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(1, (await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Total);
    }

    [Fact]
    public async Task NotificationsArePerRecipientAndCurrentApplicationAccessStillApplies()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (id, name) = await User([app.Id], ["risk.review"]);
        var (_, otherName) = await User([app.Id], ["risk.review"]);
        var policy = await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/notification-policy", new UpdateNotificationPolicy(true, [id], ["license.expiring"], [3]));
        Assert.Equal(HttpStatusCode.OK, policy.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/notification-policy", new UpdateNotificationPolicy(true, [id], ["license.expiring"], [2]))).StatusCode);
        var issued = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "notification-device"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        using var recipient = await fixture.Login(name); using var other = await fixture.Login(otherName);
        var inbox = (await recipient.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications?unreadOnly=true"))!;
        var notification = Assert.Single(inbox.Items); Assert.Equal(issued.Licenses[0].Id, notification.ResourceId);
        Assert.DoesNotContain(issued.Licenses[0].Key, notification.Message);
        Assert.Equal(0, (await other.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/admin/notifications/{notification.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsync($"/api/admin/notifications/{notification.Id}/read", null)).StatusCode);
        Assert.Equal(0, (await recipient.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications?unreadOnly=true"))!.Total);
        var renewal = new LicenseOperationRequest(Guid.NewGuid(), [issued.Licenses[0].Id], "extend", 3600, Reason: "expiry notification renewal");
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/license-operations", renewal)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var renewed = Assert.Single((await recipient.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications?unreadOnly=true"))!.Items);
        Assert.Equal(notification.ResourceId, renewed.ResourceId); Assert.NotEqual(notification.Id, renewed.Id);
        Assert.Equal(2, (await recipient.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications"))!.Total);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new UpdateAccountAccess(["risk.review"], false, []))).StatusCode);
        using var revoked = await fixture.Login(name);
        Assert.Equal(0, (await revoked.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await revoked.PostAsync($"/api/admin/notifications/{notification.Id}/read", null)).StatusCode);
    }

    private async Task<(string Id, string Name)> User(Guid[] apps, string[] permissions)
    {
        using var scope = fixture.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = new AdminUser { UserName = "reliability-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded);
        // The superseded per-user array is deliberately no longer authoritative.
        // Keep the same limited permission fixture using the role-writing adapter.
        using var admin = await fixture.Login();
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{user.Id}/access", new UpdateAccountAccess(permissions, false, apps))).StatusCode);
        return (user.Id, user.UserName);
    }

    [Fact]
    public async Task CountryHeaderIsAcceptedOnlyFromTheDirectTrustedProxyBeforeForwarding()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        await using var proxied = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Proxy:KnownProxies:0"] = "10.20.30.40", ["Risk:TrustedCountryProxies:0"] = "10.20.30.40", ["Licensing:Keys:v1"] = fixture.Services.GetRequiredService<IConfiguration>()["Licensing:Keys:v1"] })));
        foreach (var (remote, country) in new[] { ("10.20.30.40", "US"), ("10.20.30.40", "DE"), ("10.20.30.99", "CN") })
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(new ActivateLicense(app.Code, issued.Licenses[0].Key, "country-device"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var response = await proxied.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
                context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
                context.Request.Method = "POST"; context.Request.Path = "/api/client/licenses/activate";
                context.Request.ContentType = "application/json"; context.Request.ContentLength = body.Length; context.Request.Body = new MemoryStream(body);
                context.Request.Headers["X-KeyGate-Country"] = country; context.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
            });
            Assert.True(response.Response.StatusCode == 200, $"Unexpected response {response.Response.StatusCode}: {await new StreamReader(response.Response.Body).ReadToEndAsync()}");
        }
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var countries = await db.AuthorizationEvents.Where(x => x.LicenseId == issued.Licenses[0].Id).Select(x => x.TrustedCountryCode).ToArrayAsync();
        Assert.Contains("US", countries); Assert.Contains("DE", countries); Assert.DoesNotContain("CN", countries); Assert.Contains(countries, x => x is null);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var risk = Assert.Single((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items);
        Assert.Equal("country_change", risk.Rule);
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }

    [Fact]
    public async Task ForwardingProxyDoesNotEnableCountryTrustWithoutAnExplicitGeoConfiguration()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
            new Dictionary<string, string?> { ["Proxy:KnownProxies:0"] = "10.20.30.40", ["Licensing:Keys:v1"] = fixture.Services.GetRequiredService<IConfiguration>()["Licensing:Keys:v1"] })));
        var body = JsonSerializer.SerializeToUtf8Bytes(new ActivateLicense(app.Code, issued.Licenses[0].Key, "untrusted-country"));
        var response = await host.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.30.40"); context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
            context.Request.Method = "POST"; context.Request.Path = "/api/client/licenses/activate"; context.Request.ContentType = "application/json";
            context.Request.ContentLength = body.Length; context.Request.Body = new MemoryStream(body); context.Request.Headers["X-KeyGate-Country"] = "US";
        });
        Assert.Equal(200, response.Response.StatusCode);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.AuthorizationEvents.SingleAsync(x => x.LicenseId == issued.Licenses[0].Id)).TrustedCountryCode);
    }

    [Fact]
    public async Task RiskWindowDoesNotCountAnActivationAtTheExclusiveUpperBoundary()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true, 2, 10))).StatusCode);
        var start = new DateTimeOffset(fixture.Clock.GetUtcNow().UtcTicks / 10 * 10, TimeSpan.Zero).AddMinutes(-11);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuthorizationEvents.AddRange(new KeyValidation.Domain.AuthorizationEvent { ApplicationId = app.Id, LicenseId = issued.Licenses[0].Id, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = start },
                new KeyValidation.Domain.AuthorizationEvent { ApplicationId = app.Id, LicenseId = issued.Licenses[0].Id, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = start.AddMinutes(10) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items);
    }

    [Fact]
    public async Task BatchCompletionNotificationsContainOnlyCountsForTheRecipientsApplication()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (other, hidden, hiddenPlan) = await fixture.Catalog(); using var otherCleanup = other;
        var first = await ApiFixture.Issue(admin, app, plan); var second = await ApiFixture.Issue(admin, hidden, hiddenPlan);
        var (id, name) = await User([app.Id], ["licenses.read"]);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/notification-policy", new UpdateNotificationPolicy(true, [id], ["batch.completed"], []))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var requestId = Guid.NewGuid();
        var operation = new { requestId, action = "disable", licenseIds = new[] { first.Licenses[0].Id, second.Licenses[0].Id }, reason = "Grouped operation" };
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/license-operations", operation)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        using var recipient = await fixture.Login(name);
        var inbox = (await recipient.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/admin/notifications"))!;
        var notification = Assert.Single(inbox.Items, x => x.ResourceId == requestId);
        Assert.Contains("成功 1", notification.Message); Assert.DoesNotContain(second.Licenses[0].Id.ToString(), notification.Message);
        Assert.DoesNotContain(second.Licenses[0].Key, notification.Message);
    }
}
