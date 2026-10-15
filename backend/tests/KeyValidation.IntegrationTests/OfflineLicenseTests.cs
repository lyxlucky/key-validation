using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed partial class OfflineLicenseTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        Assert.Equal(status, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Error(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Forbidden)
        => Assert.Equal(code, (await Json(response, status)).GetProperty("code").GetString());
    private WebApplicationFactory<Program> Host(ECDsa signingKey, string kid = "test-v1", string? oldPublicKey = null)
    {
        var configuration = new Dictionary<string, string?>
        {
            ["Licensing:Keys:v1"] = fixture.Services.GetRequiredService<IConfiguration>()["Licensing:Keys:v1"],
            ["Offline:Enabled"] = "true", ["Offline:CurrentKeyId"] = kid, ["Offline:Keys:" + kid] = signingKey.ExportPkcs8PrivateKeyPem()
        };
        if (oldPublicKey is not null) configuration["Offline:Keys:test-v1"] = oldPublicKey;
        return fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration)));
    }
    private async Task<HttpClient> Login(WebApplicationFactory<Program> host, string name = "admin")
    {
        var client = host.CreateClient(); await ApiFixture.Csrf(client);
        await Json(await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password)));
        await ApiFixture.Csrf(client); return client;
    }
    private async Task<(HttpClient Admin, ApplicationDto App, IssuanceDto Batch, LicenseDetail Detail)> Activated(WebApplicationFactory<Program> host, long duration = 86400)
    {
        var (existing, app, _) = await fixture.Catalog(); using var existingOwner = existing;
        var plan = (await existing.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("Offline plan", duration, Features: ["basic", "export.pdf"]))).Content;
        var batch = await ApiFixture.Issue(existing, app, (await plan.ReadFromJsonAsync<PlanDto>())!);
        using var client = host.CreateClient(); await Json(await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, " raw-device ", "Offline desktop")));
        var admin = await Login(host);
        return (admin, app, batch, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!);
    }
    private static Task<HttpResponseMessage> Sign(HttpClient admin, LicenseDetail detail, int? hours = null, string device = " raw-device ")
        => admin.PostAsJsonAsync("/api/admin/offline-licenses", new { licenseId = detail.License.Id, bindingId = Assert.Single(detail.Bindings).Id, deviceId = device, hours });
    private static Task<HttpResponseMessage> Verify(HttpClient client, JsonElement issued, ApplicationDto app, string device = " raw-device ")
        => client.PostAsJsonAsync("/api/client/offline-licenses/verify", new { licenseContent = issued.GetProperty("licenseContent").GetString(), appCode = app.Code, deviceId = device });
    private static byte[] Decode(string segment) => Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/') + new string('=', (4 - segment.Length % 4) % 4));

    [Fact]
    public async Task SigningProducesRealEs256BoundToTheRawDeviceAndKeepsContentOutOfListsAndAudit()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin;
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created);
        var content = issued.GetProperty("licenseContent").GetString()!; var parts = content.Split('.'); Assert.Equal(3, parts.Length);
        var signature = Decode(parts[2]); Assert.Equal(64, signature.Length);
        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        using var header = JsonDocument.Parse(Decode(parts[0])); Assert.Equal("ES256", header.RootElement.GetProperty("alg").GetString()); Assert.Equal("KG-OFFLINE", header.RootElement.GetProperty("typ").GetString());
        using var payload = JsonDocument.Parse(Decode(parts[1])); var body = payload.RootElement;
        Assert.Equal(1, body.GetProperty("formatVersion").GetInt32()); Assert.Equal(app.Code, body.GetProperty("applicationCode").GetString());
        Assert.Equal(detail.License.Id, body.GetProperty("licenseId").GetGuid()); Assert.Equal(Assert.Single(detail.Bindings).Id, body.GetProperty("bindingId").GetGuid());
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(app.Code + "\n raw-device ")));
        Assert.Equal(expectedHash, body.GetProperty("deviceHash").GetString());
        Assert.Equal(new[] { "basic", "export.pdf" }, body.GetProperty("features").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(detail.License.ExpiresAt, body.GetProperty("expiresAt").GetDateTimeOffset());
        using var client = host.CreateClient(); await Json(await Verify(client, issued, app));
        var list = await Json(await admin.GetAsync($"/api/admin/offline-licenses?applicationId={app.Id}&licenseId={detail.License.Id}"));
        Assert.Equal("active", Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("status").GetString());
        Assert.False(list.GetRawText().Contains(content, StringComparison.Ordinal)); Assert.False(list.GetRawText().Contains("raw-device", StringComparison.Ordinal));
        var id = issued.GetProperty("record").GetProperty("id").GetGuid();
        var audit = await Json(await admin.GetAsync($"/api/admin/audit-logs?targetId={id}"));
        var log = Assert.Single(audit.GetProperty("items").EnumerateArray()); Assert.Equal("offline-license", log.GetProperty("targetType").GetString());
        Assert.Contains(detail.License.KeyHint, log.GetProperty("targetName").GetString());
        Assert.False(audit.GetRawText().Contains(content, StringComparison.Ordinal)); Assert.False(audit.GetRawText().Contains("raw-device", StringComparison.Ordinal));
        var config = await Json(await admin.GetAsync("/api/admin/offline-licenses/configuration"));
        Assert.True(config.GetProperty("configured").GetBoolean()); Assert.False(config.GetRawText().Contains("PRIVATE KEY", StringComparison.Ordinal));
        using var publicKey = ECDsa.Create(); publicKey.ImportFromPem(config.GetProperty("publicKeys").GetProperty("test-v1").GetString());
        Assert.True(publicKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public async Task ReissuingAndRevokingRejectsOldCertificatesOnline()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin; using var client = host.CreateClient();
        var first = await Json(await Sign(admin, detail, 1), HttpStatusCode.Created);
        var second = await Json(await Sign(admin, detail, 2), HttpStatusCode.Created);
        await Error(await Verify(client, first, app), "OFFLINE_LICENSE_SUPERSEDED"); await Json(await Verify(client, second, app));
        var id = second.GetProperty("record").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null)).StatusCode);
        await Error(await Verify(client, second, app), "OFFLINE_LICENSE_REVOKED");
        var list = await Json(await admin.GetAsync($"/api/admin/offline-licenses?licenseId={detail.License.Id}"));
        Assert.Equal(new[] { "revoked", "superseded" }, list.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("status").GetString()).Order());
    }

    [Theory]
    [InlineData(0)] [InlineData(169)]
    public async Task SigningRejectsOutOfRangeDuration(int hours)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, _, _, detail) = await Activated(host); using var owner = admin;
        await Error(await Sign(admin, detail, hours), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SigningNeverExceedsLicenseExpiryAndRejectsWrongOrReleasedDevice()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host, 1800); using var owner = admin; using var client = host.CreateClient();
        await Error(await Sign(admin, detail, 1, "raw-device"), "OFFLINE_DEVICE_MISMATCH");
        var issued = await Json(await Sign(admin, detail, 168), HttpStatusCode.Created);
        Assert.Equal(detail.License.ExpiresAt, issued.GetProperty("record").GetProperty("expiresAt").GetDateTimeOffset());
        await Error(await Verify(client, issued, app, "other-device"), "OFFLINE_DEVICE_MISMATCH");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{Assert.Single(detail.Bindings).Id}/release", null)).StatusCode);
        await Error(await Sign(admin, detail), "BINDING_RELEASED"); await Error(await Verify(client, issued, app), "BINDING_RELEASED");
    }

    [Theory]
    [InlineData("freeze", "LICENSE_FROZEN")]
    [InlineData("disable", "LICENSE_DISABLED")]
    [InlineData("application", "APPLICATION_DISABLED")]
    [InlineData("expiry", "LICENSE_EXPIRED")]
    public async Task LiveLicenseAndApplicationStateBlockSigningAndOnlineVerification(string state, string code)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host, 3600); using var owner = admin; using var client = host.CreateClient();
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created);
        if (state == "freeze") Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{detail.License.Id}/freeze", new { reason = "offline test" })).StatusCode);
        else if (state == "disable") Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{detail.License.Id}/disable", null)).StatusCode);
        else if (state == "application") Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/applications/{app.Id}/disable", null)).StatusCode);
        else fixture.Clock.Advance(TimeSpan.FromHours(1));
        using var refreshedAdmin = state == "expiry" ? await Login(host) : null;
        if (state == "expiry") Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync("/api/admin/auth/me")).StatusCode);
        await Error(await Sign(refreshedAdmin ?? admin, detail), code);
        await Error(await Verify(client, issued, app), state == "expiry" ? "OFFLINE_LICENSE_EXPIRED" : code);
    }

    [Fact]
    public async Task TrialAndUnactivatedLicensesCannotAcquireOfflineRights()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (original, app, plan) = await fixture.Catalog(); using var firstOwner = original; using var admin = await Login(host);
        var batch = await ApiFixture.Issue(admin, app, plan);
        await Error(await admin.PostAsJsonAsync("/api/admin/offline-licenses", new { licenseId = batch.Licenses[0].Id, bindingId = Guid.NewGuid(), deviceId = "trial" }), "OFFLINE_ACTIVATION_REQUIRED");
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = new[] { "preview" } }));
        using var client = host.CreateClient(); var trial = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "trial" }));
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{trial.GetProperty("licenseId").GetGuid()}"))!;
        await Error(await Sign(admin, detail, device: "trial"), "OFFLINE_FORMAL_LICENSE_REQUIRED");
    }

    [Fact]
    public async Task KeyRotationRetainsOldVerificationAndUsesNewKeyForNewCertificates()
    {
        using var firstKey = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var secondKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var firstHost = Host(firstKey); var (admin, app, _, detail) = await Activated(firstHost); using var owner = admin;
        var old = await Json(await Sign(admin, detail), HttpStatusCode.Created);
        await using var rotated = Host(secondKey, "test-v2", firstKey.ExportSubjectPublicKeyInfoPem()); using var client = rotated.CreateClient();
        await Json(await Verify(client, old, app)); using var newAdmin = await Login(rotated);
        var next = await Json(await Sign(newAdmin, detail), HttpStatusCode.Created); Assert.Equal("test-v2", next.GetProperty("record").GetProperty("kid").GetString());
        await Error(await Verify(client, old, app), "OFFLINE_LICENSE_SUPERSEDED");
        await using var removed = Host(secondKey, "test-v2"); using var removedClient = removed.CreateClient();
        await Error(await Verify(removedClient, old, app), "OFFLINE_KEY_UNKNOWN");
    }

    [Fact]
    public async Task UnconfiguredDisabledAndInvalidKeysFailExplicitlyWithoutAutoGeneration()
    {
        using var admin = await fixture.Login(); var config = await Json(await admin.GetAsync("/api/admin/offline-licenses/configuration"));
        Assert.False(config.GetProperty("configured").GetBoolean()); Assert.Equal("OFFLINE_UNCONFIGURED", config.GetProperty("errorCode").GetString());
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (signedAdmin, _, _, detail) = await Activated(host); using var owner = signedAdmin;
        await Error(await Sign(admin, detail), "OFFLINE_UNCONFIGURED", HttpStatusCode.ServiceUnavailable);
        await using var disabled = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?> { ["Offline:Enabled"] = "false" })));
        using var disabledAdmin = await Login(disabled); await Error(await Sign(disabledAdmin, detail), "OFFLINE_DISABLED", HttpStatusCode.ServiceUnavailable);
        await using var invalid = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, settings) => settings.AddInMemoryCollection(new Dictionary<string, string?> { ["Offline:CurrentKeyId"] = "broken", ["Offline:Keys:broken"] = "not a key" })));
        using var invalidAdmin = await Login(invalid); await Error(await Sign(invalidAdmin, detail), "OFFLINE_KEY_CONFIGURATION_INVALID", HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ReadOnlyOutOfScopeAndUnverifiedSessionsCannotSign()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin;
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created); var id = issued.GetProperty("record").GetProperty("id").GetGuid();
        using var reader = await Login(host, "readonly"); await Json(await reader.GetAsync("/api/admin/offline-licenses"));
        await Error(await Sign(reader, detail), "FORBIDDEN"); await Error(await reader.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null), "FORBIDDEN");
        var name = "offline-actor-" + Guid.NewGuid().ToString("N"); string accountId;
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>(); var user = new AdminUser { UserName = name };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded); accountId = user.Id;
        }
        await Json(await admin.PutAsJsonAsync($"/api/admin/accounts/{accountId}/access", new { allApplications = false, applicationIds = Array.Empty<Guid>() }));
        using (var restricted = await Login(host, name))
        {
            var list = await Json(await restricted.GetAsync($"/api/admin/offline-licenses?applicationId={app.Id}")); Assert.Equal(0, list.GetProperty("total").GetInt32());
            await Error(await Sign(restricted, detail), "NOT_FOUND", HttpStatusCode.NotFound);
            await Error(await restricted.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null), "NOT_FOUND", HttpStatusCode.NotFound);
        }
        await Json(await admin.PutAsJsonAsync($"/api/admin/accounts/{accountId}/access", new { allApplications = false, applicationIds = new[] { app.Id } }));
        using var actor = await Login(host, name);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var user = await db.Users.SingleAsync(x => x.Id == accountId); user.TwoFactorEnabled = true; await db.SaveChangesAsync();
        }
        await Error(await Sign(actor, detail), "UNAUTHENTICATED", HttpStatusCode.Unauthorized);
        await Error(await actor.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null), "UNAUTHENTICATED", HttpStatusCode.Unauthorized);
    }
}
