using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ClientVersionTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Error(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.BadRequest)
        => Assert.Equal(code, (await Json(response, status)).GetProperty("code").GetString());
    private static object Policy(string? minimum = "2.9.0", string? latest = "2.10.0", bool enforce = false,
        string? url = "https://updates.example.test/client.exe", string? sha256 = null) => new
        { minimumVersion = minimum, latestVersion = latest, enforceMinimum = enforce, updateUrl = url, releaseNotes = "更新说明", sha256 };
    private static Task<HttpResponseMessage> Activate(HttpClient client, ApplicationDto app, IssuanceDto batch, string? version = null)
        => client.PostAsJsonAsync("/api/client/licenses/activate", new { appCode = app.Code, licenseKey = batch.Licenses[0].Key, deviceId = "version-device", clientVersion = version });

    [Fact]
    public async Task LegacyClientsRemainAcceptedWithAnUnconfiguredPolicy()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var policy = await Json(await admin.GetAsync($"/api/admin/applications/{app.Id}/version-policy"));
        Assert.Equal(JsonValueKind.Null, policy.GetProperty("minimumVersion").ValueKind);
        Assert.False(policy.GetProperty("enforceMinimum").GetBoolean());
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var activation = await Json(await Activate(client, app, batch));
        Assert.Equal(JsonValueKind.Null, activation.GetProperty("version").GetProperty("clientVersion").ValueKind);
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.GetProperty("sessionToken").GetString());
        await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
    }

    [Fact]
    public async Task NumericComparisonOffersAnUpdateAndNeverChangesLeaseCaps()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var checksum = new string('a', 64);
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy(enforce: true, sha256: checksum)));
        var publicPolicy = await Json(await fixture.CreateClient().GetAsync($"/api/client/applications/{app.Code.ToUpperInvariant()}/version"));
        Assert.Equal(app.Code, publicPolicy.GetProperty("applicationCode").GetString());
        Assert.Equal("2.10.0", publicPolicy.GetProperty("latestVersion").GetString());
        Assert.Equal(checksum, publicPolicy.GetProperty("sha256").GetString());
        Assert.False(publicPolicy.TryGetProperty("applicationId", out _));
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var first = await Json(await Activate(client, app, batch, "2.9.0"));
        var version = first.GetProperty("version"); Assert.True(version.GetProperty("upgradeAvailable").GetBoolean());
        Assert.Equal("2.9.0", version.GetProperty("clientVersion").GetString());
        Assert.Equal(TimeSpan.FromSeconds(180), first.GetProperty("leaseExpiresAt").GetDateTimeOffset() - first.GetProperty("serverTime").GetDateTimeOffset());
        Assert.Equal(TimeSpan.FromHours(8), first.GetProperty("absoluteExpiresAt").GetDateTimeOffset() - first.GetProperty("serverTime").GetDateTimeOffset());
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.GetProperty("sessionToken").GetString());
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        client.DefaultRequestHeaders.Add("X-Client-Version", "2.10.0");
        var next = await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        Assert.False(next.GetProperty("version").GetProperty("upgradeAvailable").GetBoolean());
        Assert.Equal(first.GetProperty("absoluteExpiresAt").GetDateTimeOffset(), next.GetProperty("absoluteExpiresAt").GetDateTimeOffset());
        client.DefaultRequestHeaders.Remove("X-Client-Version");
        var retained = await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        Assert.Equal("2.10.0", retained.GetProperty("version").GetProperty("clientVersion").GetString());
    }

    [Fact]
    public async Task EnforcedPolicyImmediatelyBlocksExistingSessionsWithoutExtendingThem()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var first = await Json(await Activate(client, app, batch, "2.9.0"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.GetProperty("sessionToken").GetString());
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy("2.10.0", "2.10.0", true)));
        var rejected = await Json(await client.PostAsync("/api/client/sessions/heartbeat", null), HttpStatusCode.UpgradeRequired);
        Assert.Equal("CLIENT_UPGRADE_REQUIRED", rejected.GetProperty("code").GetString());
        Assert.Equal("2.10.0", rejected.GetProperty("version").GetProperty("minimumVersion").GetString());
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Equal(first.GetProperty("leaseExpiresAt").GetDateTimeOffset(), Assert.Single(Assert.Single(detail.Bindings).Sessions).LeaseExpiresAt);
        client.DefaultRequestHeaders.Add("X-Client-Version", "2.10.0");
        await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
    }

    [Fact]
    public async Task MissingVersionIsRejectedOnlyWhenEnforcedForFormalTrialAndHeartbeat()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var old = await Json(await Activate(client, app, batch));
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy(enforce: true)));
        await Error(await Activate(client, app, batch), "CLIENT_VERSION_REQUIRED", HttpStatusCode.UpgradeRequired);
        await Error(await Activate(client, app, batch, "2.8.9999"), "CLIENT_UPGRADE_REQUIRED", HttpStatusCode.UpgradeRequired);
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "trial-version" }), "CLIENT_VERSION_REQUIRED", HttpStatusCode.UpgradeRequired);
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "trial-version", clientVersion = "2.8.0" }), "CLIENT_UPGRADE_REQUIRED", HttpStatusCode.UpgradeRequired);
        var trial = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "trial-version", clientVersion = "2.10.0" }));
        Assert.Equal("2.10.0", trial.GetProperty("version").GetProperty("clientVersion").GetString());
        client.DefaultRequestHeaders.Authorization = new("Bearer", old.GetProperty("sessionToken").GetString());
        await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "CLIENT_VERSION_REQUIRED", HttpStatusCode.UpgradeRequired);
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3-beta")]
    [InlineData("10000.0.0")]
    [InlineData("-1.0.0")]
    [InlineData("1.2.3 ")]
    [InlineData("01.2.3")]
    [InlineData("")]
    public async Task MalformedVersionsAreRejectedBeforeStartingAuthorization(string version)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        await Error(await Activate(client, app, batch, version), "CLIENT_VERSION_INVALID");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Null(detail.License.ActivatedAt); Assert.Empty(detail.Bindings);
    }

    [Theory]
    [InlineData("2.10.0", "2.9.0", "https://example.test/file", null)]
    [InlineData("0.0.0", "10000.0.0", "https://example.test/file", null)]
    [InlineData("0.0.0", "1.0.0", "http://example.test/file", null)]
    [InlineData("0.0.0", "1.0.0", "javascript:alert(1)", null)]
    [InlineData("0.0.0", "1.0.0", "https://example.test/file", "bad")]
    public async Task InvalidPoliciesDoNotPersist(string minimum, string latest, string url, string? checksum)
    {
        var (admin, app, _) = await fixture.Catalog(); using var owner = admin;
        await Error(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy(minimum, latest, url: url, sha256: checksum)), "VALIDATION_ERROR");
        var policy = await Json(await admin.GetAsync($"/api/admin/applications/{app.Id}/version-policy"));
        Assert.Equal(JsonValueKind.Null, policy.GetProperty("latestVersion").ValueKind);
    }

    [Fact]
    public async Task PolicyRequiresPlatformPermissionAndWritesReadableBeforeAfterAudit()
    {
        var (admin, app, _) = await fixture.Catalog(); using var owner = admin; using var ordinary = await fixture.Login("operator");
        await Error(await ordinary.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy()), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy()));
        var logs = await Json(await admin.GetAsync($"/api/admin/audit-logs?targetId={app.Id}&action=application.version-policy.update"));
        var log = Assert.Single(logs.GetProperty("items").EnumerateArray());
        Assert.Equal("application", log.GetProperty("targetType").GetString()); Assert.Equal(app.Name, log.GetProperty("targetName").GetString());
        using var changes = JsonDocument.Parse(log.GetProperty("changes").GetString()!);
        Assert.False(changes.RootElement.GetProperty("before").GetProperty("enforceMinimum").GetBoolean());
        Assert.Equal("2.10.0", changes.RootElement.GetProperty("after").GetProperty("latestVersion").GetString());
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.AuthorizationEvents.AnyAsync(x => x.ApplicationId == app.Id));
    }

    [Fact]
    public async Task PlatformOnlyCustomRoleCanReadAndEditItsPolicyWithoutLicenseReadPermission()
    {
        var (admin, app, _) = await fixture.Catalog(); using var owner = admin;
        var (otherAdmin, hidden, _) = await fixture.Catalog(); using var otherOwner = otherAdmin;
        var name = "version-manager-" + Guid.NewGuid().ToString("N");
        await Json(await admin.PostAsJsonAsync("/api/admin/roles", new { name, permissions = new[] { "platform.manage" } }), HttpStatusCode.Created);
        string id;
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>(); var user = new AdminUser { UserName = name };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, name)).Succeeded); id = user.Id;
        }
        await Json(await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = false, applicationIds = new[] { app.Id } }));
        using var manager = await fixture.Login(name);
        var access = await manager.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"); Assert.Equal(new[] { "platform.manage" }, access!.Permissions);
        await Json(await manager.GetAsync($"/api/admin/applications/{app.Id}/version-policy"));
        await Json(await manager.PutAsJsonAsync($"/api/admin/applications/{app.Id}/version-policy", Policy(enforce: true)));
        await Error(await manager.GetAsync("/api/admin/licenses"), "FORBIDDEN", HttpStatusCode.Forbidden);
        await Error(await manager.GetAsync($"/api/admin/applications/{hidden.Id}/version-policy"), "NOT_FOUND", HttpStatusCode.NotFound);
        await Error(await manager.PutAsJsonAsync($"/api/admin/applications/{hidden.Id}/version-policy", Policy()), "NOT_FOUND", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LoopbackHttpNeedsAnExplicitTestingOrDevelopmentException()
    {
        var (admin, app, _) = await fixture.Catalog(); using var owner = admin; var path = $"/api/admin/applications/{app.Id}/version-policy";
        await Error(await admin.PutAsJsonAsync(path, Policy(url: "http://127.0.0.1/update.exe")), "VALIDATION_ERROR");
        foreach (var environment in new[] { "Testing", "Production" })
        {
            await using var host = fixture.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environment); builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["ClientVersions:AllowLoopbackHttp"] = "true" }));
            });
            using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
            await ApiFixture.Csrf(client); await Json(await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))); await ApiFixture.Csrf(client);
            var response = await client.PutAsJsonAsync(path, Policy(url: "http://127.0.0.1/update.exe"));
            if (environment == "Testing") await Json(response); else await Error(response, "VALIDATION_ERROR");
        }
    }

    [Fact]
    public async Task MalformedHeartbeatVersionCannotChangeTheStoredVersionOrLease()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var activation = await Json(await Activate(client, app, batch, "9999.9999.9999"));
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.GetProperty("sessionToken").GetString());
        fixture.Clock.Advance(TimeSpan.FromSeconds(20)); client.DefaultRequestHeaders.Add("X-Client-Version", "2.10.0-beta");
        await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "CLIENT_VERSION_INVALID");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Equal(activation.GetProperty("leaseExpiresAt").GetDateTimeOffset(), Assert.Single(Assert.Single(detail.Bindings).Sessions).LeaseExpiresAt);
        client.DefaultRequestHeaders.Remove("X-Client-Version");
        var next = await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        Assert.Equal("9999.9999.9999", next.GetProperty("version").GetProperty("clientVersion").GetString());
    }
}
