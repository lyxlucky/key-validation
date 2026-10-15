using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class RebindingTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OpenApiDescribesCurrentDeviceBearerAndPolicyContracts()
    {
        using var client = fixture.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var release = document.GetProperty("paths").GetProperty("/api/client/device-bindings/release").GetProperty("post");
        Assert.True(release.GetProperty("security")[0].TryGetProperty("ClientBearer", out _));
        Assert.False(release.TryGetProperty("requestBody", out _));
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("DevicePolicyDto", out _));
    }

    [Fact]
    public async Task NewLicensesHaveDefaultPolicyAndRepeatedReleaseDoesNotConsumeAgain()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var response = await admin.GetAsync($"/api/admin/licenses/{id}/device-policy");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var policy = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(policy.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, policy.GetProperty("maxChanges").GetInt32());
        Assert.Equal(30, policy.GetProperty("windowDays").GetInt32());
        Assert.Equal(24, policy.GetProperty("cooldownHours").GetInt32());
        using var client = fixture.CreateClient();
        var activation = await Activate(client, app, batch, "first");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        var binding = Assert.Single(detail.Bindings);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{binding.Id}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{binding.Id}/release", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.SessionToken);
        Assert.False((await client.PostAsync("/api/client/sessions/heartbeat", null)).IsSuccessStatusCode);
        policy = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{id}/device-policy");
        Assert.Equal(1, policy.GetProperty("usedChanges").GetInt32());
        Assert.Equal(2, policy.GetProperty("remainingChanges").GetInt32());
        await Activate(client, app, batch, "second");
        detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        var second = Assert.Single(detail.Bindings, x => x.ReleasedAt is null);
        await AssertCode(await admin.PostAsync($"/api/admin/device-bindings/{second.Id}/release", null), HttpStatusCode.Conflict, "REBIND_COOLDOWN");
    }

    [Fact]
    public async Task ClientReleaseRequiresLiveSessionAndCannotChooseAnotherBinding()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        using var client = fixture.CreateClient();
        await AssertCode(await client.PostAsync("/api/client/device-bindings/release", null), HttpStatusCode.Unauthorized, "SESSION_INVALID");
        var activation = await Activate(client, app, batch, "first");
        var otherBatch = await ApiFixture.Issue(admin, app, plan);
        using var otherClient = fixture.CreateClient();
        var otherActivation = await Activate(otherClient, app, otherBatch, "other");
        var otherDetail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{otherBatch.Licenses[0].Id}"))!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/client/device-bindings/release", new { bindingId = otherDetail.Bindings[0].Id })).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.NotNull(Assert.Single(detail.Bindings).ReleasedAt);
        Assert.NotNull(Assert.Single(detail.Bindings[0].Sessions).RevokedAt);
        Assert.False((await client.PostAsync("/api/client/device-bindings/release", null)).IsSuccessStatusCode);
        otherClient.DefaultRequestHeaders.Authorization = new("Bearer", otherActivation.SessionToken);
        Assert.Equal(HttpStatusCode.OK, (await otherClient.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        otherDetail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{otherBatch.Licenses[0].Id}"))!;
        Assert.Null(Assert.Single(otherDetail.Bindings).ReleasedAt);
    }

    [Fact]
    public async Task ConfigurationKeepsUsageAndResetRetainsHistoryEvenAtSameTimestamp()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var path = $"/api/admin/licenses/{id}/device-policy";
        using var client = fixture.CreateClient();
        var first = await Activate(client, app, batch, "first");
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        var changed = await admin.PatchAsJsonAsync(path, new { enabled = false, maxChanges = 1, windowDays = 30, cooldownHours = 24, reason = " temporary exception " });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var policy = await changed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, policy.GetProperty("usedChanges").GetInt32());
        Assert.Equal(JsonValueKind.Null, policy.GetProperty("remainingChanges").ValueKind);
        var second = await Activate(client, app, batch, "second");
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        var reset = await admin.PostAsJsonAsync(path + "/reset", new { reason = " verified device replacement " });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        policy = await reset.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, policy.GetProperty("usedChanges").GetInt32());
        Assert.Equal(JsonValueKind.Null, policy.GetProperty("lastChangedAt").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(path, new { enabled = true, maxChanges = 1, windowDays = 30, cooldownHours = 24, reason = " restore policy " })).StatusCode);
        var third = await Activate(client, app, batch, "third");
        Assert.Equal(first.ExpiresAt, third.ExpiresAt);
        client.DefaultRequestHeaders.Authorization = new("Bearer", third.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        policy = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(1, policy.GetProperty("usedChanges").GetInt32());
        var fourth = await Activate(client, app, batch, "fourth");
        client.DefaultRequestHeaders.Authorization = new("Bearer", fourth.SessionToken);
        await AssertCode(await client.PostAsync("/api/client/device-bindings/release", null), HttpStatusCode.Conflict, "REBIND_LIMIT_REACHED");
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(3, await db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"t_rebinding_events\" WHERE \"LicenseId\" = {id}").SingleAsync());
        var audits = await db.AuditLogs.Where(x => x.TargetId == id && x.Action == "license.device-policy.reset").ToListAsync();
        Assert.Single(audits);
        Assert.Contains("verified device replacement", audits[0].Changes);
    }

    [Theory]
    [InlineData(0, 30, 24, "reason")]
    [InlineData(101, 30, 24, "reason")]
    [InlineData(3, 0, 24, "reason")]
    [InlineData(3, 366, 24, "reason")]
    [InlineData(3, 30, -1, "reason")]
    [InlineData(3, 30, 8761, "reason")]
    [InlineData(3, 30, 24, "   ")]
    public async Task InvalidPoliciesNeverMutate(int maxChanges, int windowDays, int cooldownHours, string reason)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var path = $"/api/admin/licenses/{batch.Licenses[0].Id}/device-policy";
        await AssertCode(await admin.PatchAsJsonAsync(path, new { enabled = true, maxChanges, windowDays, cooldownHours, reason }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var policy = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(3, policy.GetProperty("maxChanges").GetInt32());
        Assert.Equal(30, policy.GetProperty("windowDays").GetInt32());
        Assert.Equal(24, policy.GetProperty("cooldownHours").GetInt32());
    }

    [Theory]
    [InlineData(1, 0, "REBIND_LIMIT_REACHED")]
    [InlineData(3, 24, "REBIND_COOLDOWN")]
    public async Task ReleasesAcrossAccountsCannotRacePastQuotaOrCooldown(int maxChanges, int cooldownHours, string code)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("two devices", 86400, 2, true))).StatusCode);
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/licenses/{id}/device-policy", new { enabled = true, maxChanges, windowDays = 30, cooldownHours, reason = " concurrency test " })).StatusCode);
        using var client = fixture.CreateClient();
        await Activate(client, app, batch, "first"); await Activate(client, app, batch, "second");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        using var other = await fixture.Login("operator");
        var responses = await Task.WhenAll(admin.PostAsync($"/api/admin/device-bindings/{detail.Bindings[0].Id}/release", null), other.PostAsync($"/api/admin/device-bindings/{detail.Bindings[1].Id}/release", null));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.NoContent);
        await AssertCode(Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict), HttpStatusCode.Conflict, code);
        detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        Assert.Single(detail.Bindings, x => x.ReleasedAt is not null);
        Assert.Single(detail.Bindings.SelectMany(x => x.Sessions), x => x.RevokedAt is not null);
        var policy = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{id}/device-policy");
        Assert.Equal(1, policy.GetProperty("usedChanges").GetInt32());
    }

    [Theory]
    [InlineData(1, 1, 0, "REBIND_LIMIT_REACHED")]
    [InlineData(3, 30, 24, "REBIND_COOLDOWN")]
    public async Task ReleaseIsAllowedExactlyAtWindowOrCooldownBoundary(int maxChanges, int windowDays, int cooldownHours, string code)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("long duration", 31536000, 1, true))).StatusCode);
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var path = $"/api/admin/licenses/{id}/device-policy";
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(path, new { enabled = true, maxChanges, windowDays, cooldownHours, reason = " boundary test " })).StatusCode);
        using var client = fixture.CreateClient();
        var first = await Activate(client, app, batch, "first");
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMicroseconds(1));
        var second = await Activate(client, app, batch, "second");
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionToken);
        await AssertCode(await client.PostAsync("/api/client/device-bindings/release", null), HttpStatusCode.Conflict, code);
        fixture.Clock.Advance(TimeSpan.FromMicroseconds(1));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
    }

    [Theory]
    [InlineData("lease", "SESSION_EXPIRED", 401)]
    [InlineData("session", "SESSION_REVOKED", 401)]
    [InlineData("license", "LICENSE_DISABLED", 403)]
    [InlineData("application", "APPLICATION_DISABLED", 403)]
    [InlineData("expiry", "LICENSE_EXPIRED", 403)]
    public async Task InvalidClientProofNeverReleasesBinding(string invalidation, string code, int status)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        using var client = fixture.CreateClient();
        var activation = await Activate(client, app, batch, "first");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.SessionToken);
        switch (invalidation)
        {
            case "lease": fixture.Clock.Advance(TimeSpan.FromSeconds(180)); break;
            case "expiry": fixture.Clock.Advance(TimeSpan.FromDays(1)); break;
            case "session": Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/sessions/{detail.Bindings[0].Sessions[0].Id}/revoke", null)).StatusCode); break;
            case "license": Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{id}/disable", null)).StatusCode); break;
            case "application": Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/applications/{app.Id}/disable", null)).StatusCode); break;
        }
        await AssertCode(await client.PostAsync("/api/client/device-bindings/release", null), (HttpStatusCode)status, code);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.Bindings.SingleAsync(x => x.Id == detail.Bindings[0].Id)).ReleasedAt);
        Assert.False(await db.RebindingEvents.AnyAsync(x => x.LicenseId == id));
    }

    [Fact]
    public async Task PolicyWritesRequireOperateRoleCsrfAndTrimmedReason()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var path = $"/api/admin/licenses/{batch.Licenses[0].Id}/device-policy";
        using var reader = await fixture.Login("readonly");
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync(path)).StatusCode);
        var update = new { enabled = true, maxChanges = 2, windowDays = 10, cooldownHours = 0, reason = "policy" };
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PatchAsJsonAsync(path, update)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(path + "/reset", new { reason = "reset" })).StatusCode);
        await AssertCode(await admin.PostAsJsonAsync(path + "/reset", new { reason = "   " }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await AssertCode(await admin.PostAsJsonAsync(path + "/reset", new { reason = new string('a', 501) }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        using var op = await fixture.Login("operator");
        Assert.Equal(HttpStatusCode.OK, (await op.PatchAsJsonAsync(path, update)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await op.PostAsJsonAsync(path + "/reset", new { reason = "reset" })).StatusCode);
        op.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PatchAsJsonAsync(path, update)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await op.PostAsJsonAsync(path + "/reset", new { reason = "reset" })).StatusCode);
    }

    [Fact]
    public async Task AuditRetentionDoesNotEraseRebindingHistoryOrRestoreQuota()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        using var client = fixture.CreateClient();
        var activation = await Activate(client, app, batch, "first");
        client.DefaultRequestHeaders.Authorization = new("Bearer", activation.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        var path = $"/api/admin/licenses/{id}/device-policy";
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync(path, new { enabled = true, maxChanges = 1, windowDays = 365, cooldownHours = 0, reason = " long rolling window " })).StatusCode);
        var auditId = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.RebindingEvents.SingleAsync(x => x.LicenseId == id)).ChangedAt = fixture.Clock.GetUtcNow().AddDays(-181);
            db.AuditLogs.Add(new() { Id = auditId, ActorId = "retention", Action = "test", TargetId = id, CreatedAt = fixture.Clock.GetUtcNow().AddDays(-181), TraceId = "retention" });
            await db.SaveChangesAsync();
        }
        using var worker = new RetentionWorker(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Clock,
            fixture.Services.GetRequiredService<IConfiguration>(), NullLogger<RetentionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var removed = false;
        for (var i = 0; i < 40 && !removed; i++)
        {
            using var scope = fixture.Services.CreateScope();
            removed = !await scope.ServiceProvider.GetRequiredService<AppDbContext>().AuditLogs.AnyAsync(x => x.Id == auditId);
            if (!removed) await Task.Delay(25);
        }
        await worker.StopAsync(CancellationToken.None);
        Assert.True(removed);
        var policy = await admin.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(1, policy.GetProperty("usedChanges").GetInt32());
        Assert.Equal(0, policy.GetProperty("remainingChanges").GetInt32());
        var second = await Activate(client, app, batch, "second");
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionToken);
        await AssertCode(await client.PostAsync("/api/client/device-bindings/release", null), HttpStatusCode.Conflict, "REBIND_LIMIT_REACHED");
    }

    private static async Task<ActivationDto> Activate(HttpClient client, ApplicationDto app, IssuanceDto batch, string device)
    {
        var response = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, device));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ActivationDto>())!;
    }

    private static async Task AssertCode(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
