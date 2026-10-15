using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class AuthorizationPolicyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Error(HttpResponseMessage response, string code, HttpStatusCode status = HttpStatusCode.Forbidden)
        => Assert.Equal(code, (await Json(response, status)).GetProperty("code").GetString());
    private async Task<(HttpClient Admin, ApplicationDto App, PlanDto Plan, IssuanceDto Batch)> Card(int devices = 3, int sessions = 1, int? deadline = null)
    {
        var (admin, app, _) = await fixture.Catalog();
        var response = await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new { name = "策略卡", durationSeconds = 86400, maxDevices = devices, maxConcurrentSessions = sessions, activationValidityDays = deadline, features = new[] { " Export.Pdf ", "export.pdf", "basic" } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<PlanDto>())!;
        return (admin, app, plan, await ApiFixture.Issue(admin, app, plan));
    }
    private static Task<HttpResponseMessage> Activate(HttpClient client, ApplicationDto app, IssuanceDto batch, string device)
        => client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, device));
    private static void Bearer(HttpClient client, JsonElement session) => client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("sessionToken").GetString());

    [Fact] public async Task ConcurrentSessionCapacityIsIndependentFromDeviceBindingsAndLogoutReclaimsIt()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var first = await Json(await Activate(client, app, batch, "A"));
        await Error(await Activate(client, app, batch, "B"), "SESSION_LIMIT_REACHED");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Single(detail.Bindings);
        Bearer(client, first); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/sessions/logout", null)).StatusCode);
        await Json(await Activate(client, app, batch, "B"));
        detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Equal(2, detail.Bindings.Count);
        Assert.Single(detail.Bindings.SelectMany(x => x.Sessions), x => x.RevokedAt is null);
    }

    [Fact] public async Task SimultaneousDevicesCannotOverbookAndSameDeviceCanReplaceSession()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => Activate(client, app, batch, "race-" + i)));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses.Where(x => x.StatusCode != HttpStatusCode.OK)) await Error(response, "SESSION_LIMIT_REACHED");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Single(detail.Bindings);
        fixture.Clock.Advance(TimeSpan.FromSeconds(180));
        var replacement = await Json(await Activate(client, app, batch, "replacement"));
        var same = await Json(await Activate(client, app, batch, "replacement"));
        Assert.NotEqual(replacement.GetProperty("sessionToken").GetString(), same.GetProperty("sessionToken").GetString());
        Bearer(client, replacement); await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "SESSION_REVOKED", HttpStatusCode.Unauthorized);
    }

    [Fact] public async Task FreezeRevokesSessionsPausesExactRemainingTimeAndKeepsOneHistoryEntry()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient(); var id = batch.Licenses[0].Id;
        var first = await Json(await Activate(client, app, batch, "A")); Bearer(client, first);
        fixture.Clock.Advance(TimeSpan.FromMilliseconds(1234));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "售后暂停" })).StatusCode);
        var frozen = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{id}");
        Assert.Equal("frozen", frozen.GetProperty("license").GetProperty("status").GetString());
        Assert.Equal(86398.766, frozen.GetProperty("license").GetProperty("frozenRemainingSeconds").GetDouble(), 5);
        await Error(await Activate(client, app, batch, "B"), "LICENSE_FROZEN");
        await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "LICENSE_FROZEN");
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        using var refreshedAdmin = await fixture.Login(); admin = refreshedAdmin;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "重复冻结" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/unfreeze", new { reason = "继续使用" })).StatusCode);
        var unfrozen = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        Assert.Equal(first.GetProperty("expiresAt").GetDateTimeOffset().AddDays(2), unfrozen.License.ExpiresAt);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/unfreeze", new { reason = "重复解冻" })).StatusCode);
        await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "SESSION_REVOKED", HttpStatusCode.Unauthorized);
        var history = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{id}/freeze-history");
        var entry = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal("售后暂停", entry.GetProperty("freezeReason").GetString()); Assert.Equal("继续使用", entry.GetProperty("unfreezeReason").GetString());
    }

    [Fact] public async Task DeadlineEqualityRejectsUnusedCardsWithoutBindingOrStartingLifetime()
    {
        var (admin, app, _, batch) = await Card(deadline: 1); using var adminDispose = admin; using var client = fixture.CreateClient();
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        using var refreshedAdmin = await fixture.Login(); admin = refreshedAdmin;
        await Error(await Activate(client, app, batch, "A"), "ACTIVATION_DEADLINE_PASSED");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Null(detail.License.ActivatedAt); Assert.Empty(detail.Bindings); Assert.Equal("activation_expired", detail.License.Status);
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses?applicationId={app.Id}&status=activation_expired");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == batch.Licenses[0].Id);
    }

    [Fact] public async Task IssuedPolicyAndFeaturesAreSnapshotsAndHeartbeatReturnsEntitlements()
    {
        var (admin, app, plan, batch) = await Card(deadline: 2); using var adminDispose = admin; using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new { name = "修改", durationSeconds = 7200, maxDevices = 5, maxConcurrentSessions = 4, activationValidityDays = 9, features = new[] { "new" }, isEnabled = true })).StatusCode);
        var first = await Json(await Activate(client, app, batch, "A"));
        Assert.Equal(batch.Licenses[0].Id, first.GetProperty("licenseId").GetGuid()); Assert.False(first.GetProperty("isTrial").GetBoolean());
        Assert.Equal(new[] { "export.pdf", "basic" }, first.GetProperty("features").EnumerateArray().Select(x => x.GetString()));
        Bearer(client, first); var heartbeat = await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        Assert.Equal(first.GetProperty("features").GetRawText(), heartbeat.GetProperty("features").GetRawText());
        await Error(await Activate(client, app, batch, "B"), "SESSION_LIMIT_REACHED");
    }

    [Theory] [InlineData(0, null)] [InlineData(101, null)] [InlineData(1, 0)] [InlineData(1, 3651)]
    public async Task InvalidPlanPoliciesAreRejectedWithoutMutation(int sessions, int? deadline)
    {
        var (admin, app, _) = await fixture.Catalog(); using var adminDispose = admin;
        await Error(await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new { name = "invalid", durationSeconds = 3600, maxDevices = 1, maxConcurrentSessions = sessions, activationValidityDays = deadline }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
    }

    [Fact] public async Task TrialRetryKeepsOriginalExpiryAndFeaturesThenFormalActivationUpgradesAtomically()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var url = $"/api/admin/applications/{app.Id}/trial-policy";
        var policy = await Json(await admin.GetAsync(url)); Assert.False(policy.GetProperty("isEnabled").GetBoolean()); Assert.Equal(3600, policy.GetProperty("durationSeconds").GetInt64());
        await Json(await admin.PutAsJsonAsync(url, new { isEnabled = true, durationSeconds = 3600, features = new[] { "trial.view" } }));
        var first = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "A" }));
        Assert.True(first.GetProperty("isTrial").GetBoolean());
        var trialId = first.GetProperty("licenseId").GetGuid();
        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{trialId}"); Assert.Equal(JsonValueKind.Null, detail.GetProperty("license").GetProperty("batchId").ValueKind);
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await Json(await admin.PutAsJsonAsync(url, new { isEnabled = true, durationSeconds = 7200, features = new[] { "changed" } }));
        var retry = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "A" }));
        Assert.Equal(first.GetProperty("expiresAt").GetString(), retry.GetProperty("expiresAt").GetString()); Assert.Equal(trialId, retry.GetProperty("licenseId").GetGuid());
        Assert.Equal("trial.view", Assert.Single(retry.GetProperty("features").EnumerateArray()).GetString());
        await Error(await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, "invalid", "A")), "LICENSE_INVALID");
        Bearer(client, retry); await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        var formal = await Json(await Activate(client, app, batch, "A")); Assert.False(formal.GetProperty("isTrial").GetBoolean());
        await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "SESSION_REVOKED", HttpStatusCode.Unauthorized);
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "A" }), "TRIAL_UPGRADED");
    }

    [Fact] public async Task ConcurrentTrialClaimsDeduplicateAndClosedPolicyDoesNotRevokeExistingSession()
    {
        var (admin, app, _) = await fixture.Catalog(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var url = $"/api/admin/applications/{app.Id}/trial-policy";
        await Json(await admin.PutAsJsonAsync(url, new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "same" })));
        var sessions = new List<JsonElement>(); foreach (var response in responses) sessions.Add(await Json(response));
        Assert.Single(sessions.Select(x => x.GetProperty("licenseId").GetGuid()).Distinct());
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{sessions[0].GetProperty("licenseId").GetGuid()}"))!;
        Assert.Single(Assert.Single(detail.Bindings).Sessions, x => x.RevokedAt is null);
        var current = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "same" })); Bearer(client, current);
        await Json(await admin.PutAsJsonAsync(url, new { isEnabled = false, durationSeconds = 3600, features = Array.Empty<string>() }));
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "same" }), "TRIAL_DISABLED");
        await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        await Json(await admin.PutAsJsonAsync(url, new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "same" }), "TRIAL_EXPIRED");
    }

    [Fact] public async Task FreezeSerializesWithActivationAndHeartbeatAndIsExcludedFromActiveQueries()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient(); var id = batch.Licenses[0].Id;
        var first = await Json(await Activate(client, app, batch, "A")); Bearer(client, first);
        var freeze = admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "竞争" });
        var activate = Activate(client, app, batch, "A"); var heartbeat = client.PostAsync("/api/client/sessions/heartbeat", null);
        await Task.WhenAll(freeze, activate, heartbeat); Assert.Equal(HttpStatusCode.NoContent, (await freeze).StatusCode);
        await Error(await Activate(client, app, batch, "A"), "LICENSE_FROZEN"); await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "LICENSE_FROZEN");
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        Assert.All(detail.Bindings.SelectMany(x => x.Sessions), x => Assert.NotNull(x.RevokedAt));
        var frozen = (await admin.GetFromJsonAsync<PagedResult<LicenseDto>>($"/api/admin/licenses?applicationId={app.Id}&status=frozen"))!; Assert.Single(frozen.Items);
        var active = (await admin.GetFromJsonAsync<PagedResult<LicenseDto>>($"/api/admin/licenses?applicationId={app.Id}&status=active"))!; Assert.Empty(active.Items);
        var expiring = (await admin.GetFromJsonAsync<ExpiringLicensesDto>($"/api/admin/expiring-licenses?applicationId={app.Id}&withinDays=3"))!; Assert.Empty(expiring.Items);
        var renewal = await Json(await admin.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(Guid.NewGuid(), [id], "extend", 3600, Reason: "续期")));
        Assert.Equal("LICENSE_FROZEN", Assert.Single(renewal.GetProperty("items").EnumerateArray()).GetProperty("errorCode").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{id}/disable", null)).StatusCode);
        await Error(await Activate(client, app, batch, "A"), "LICENSE_DISABLED");
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{id}/enable", null)).StatusCode);
        await Error(await Activate(client, app, batch, "A"), "LICENSE_FROZEN");
    }

    [Fact] public async Task PolicyWritesRequireRolesAndCsrfAndValidateBeforeMutation()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var id = batch.Licenses[0].Id; var policyUrl = $"/api/admin/applications/{app.Id}/trial-policy";
        using var read = await fixture.Login("readonly"); using var op = await fixture.Login("operator");
        await Error(await read.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "x" }), "FORBIDDEN");
        await Error(await op.PutAsJsonAsync(policyUrl, new { isEnabled = true, durationSeconds = 1 }), "FORBIDDEN");
        Assert.Equal(HttpStatusCode.OK, (await read.GetAsync(policyUrl)).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        await Error(await admin.PutAsJsonAsync(policyUrl, new { isEnabled = true, durationSeconds = 1 }), "CSRF_INVALID", HttpStatusCode.BadRequest);
        await ApiFixture.Csrf(admin);
        foreach (var duration in new long[] { 0, 2592001 }) await Error(await admin.PutAsJsonAsync(policyUrl, new { isEnabled = true, durationSeconds = duration }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        await Error(await admin.PutAsJsonAsync(policyUrl, new { durationSeconds = 3600 }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        await Error(await admin.PutAsJsonAsync(policyUrl, new { isEnabled = true, durationSeconds = 3600, features = new[] { "invalid value" } }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>(policyUrl)).GetProperty("isEnabled").GetBoolean());
        await Error(await op.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "   " }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        await Error(await op.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = new string('x', 501) }), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        await Error(await op.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "未激活" }), "LICENSE_NOT_FREEZABLE");
        await Json(await Activate(client, app, batch, "A"));
        Assert.Equal(HttpStatusCode.NoContent, (await op.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = " 操作员 " })).StatusCode);
        var history = await Json(await read.GetAsync($"/api/admin/licenses/{id}/freeze-history")); Assert.Equal("操作员", Assert.Single(history.GetProperty("items").EnumerateArray()).GetProperty("freezeReason").GetString());
        await Error(await read.GetAsync($"/api/admin/licenses/{id}/freeze-history?pageSize=101"), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
    }

    [Fact] public async Task FormalSessionLimitFailurePreservesTrialAndExpiredTrialCanUpgradeLater()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = new[] { "trial" } }));
        var trial = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "A" }));
        var formal = await Json(await Activate(client, app, batch, "B"));
        await Error(await Activate(client, app, batch, "A"), "SESSION_LIMIT_REACHED");
        Bearer(client, trial); await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var claim = await db.TrialClaims.SingleAsync(x => x.ApplicationId == app.Id); Assert.Null(claim.UpgradedAt);
            Assert.Equal(1, await db.Bindings.CountAsync(x => x.LicenseId == batch.Licenses[0].Id));
        }
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        await Json(await Activate(client, app, batch, "A"));
        using var finalScope = fixture.Services.CreateScope();
        var upgraded = await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().TrialClaims.SingleAsync(x => x.ApplicationId == app.Id);
        Assert.Equal(batch.Licenses[0].Id, upgraded.UpgradedToLicenseId); Assert.NotNull(upgraded.UpgradedAt);
    }

    [Fact] public async Task DatabaseFailureAfterFormalActivationFlushRollsBackLifetimeBindingsAndTrialRevocation()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        var trial = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "rollback" }));
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE FUNCTION policy_test_reject_upgrade() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN RAISE EXCEPTION ''injected upgrade failure''; END'");
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER policy_test_reject_upgrade BEFORE UPDATE ON \"TrialClaims\" FOR EACH ROW EXECUTE FUNCTION policy_test_reject_upgrade()");
        try
        {
            await Error(await Activate(client, app, batch, "rollback"), "INTERNAL_ERROR", HttpStatusCode.InternalServerError);
            Bearer(client, trial); await Json(await client.PostAsync("/api/client/sessions/heartbeat", null));
            var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
            Assert.Null(detail.License.ActivatedAt); Assert.Empty(detail.Bindings);
            Assert.Null((await db.TrialClaims.AsNoTracking().SingleAsync(x => x.ApplicationId == app.Id)).UpgradedAt);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER policy_test_reject_upgrade ON \"TrialClaims\"");
            await db.Database.ExecuteSqlRawAsync("DROP FUNCTION policy_test_reject_upgrade()");
        }
    }

    [Fact] public async Task InitialTrialRacingFormalActivationCannotEscapeUpgradeAndReleasedPaidDevicesStayIneligible()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        var trialTask = client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "race" });
        var formalTask = Activate(client, app, batch, "race"); await Task.WhenAll(trialTask, formalTask);
        var formal = await Json(await formalTask); var trialResponse = await trialTask;
        if (trialResponse.StatusCode == HttpStatusCode.OK)
        {
            var trial = await Json(trialResponse); Bearer(client, trial); await Error(await client.PostAsync("/api/client/sessions/heartbeat", null), "SESSION_REVOKED", HttpStatusCode.Unauthorized);
        }
        else await Error(trialResponse, "TRIAL_UPGRADED");
        Bearer(client, formal); Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "race" }), "TRIAL_UPGRADED");
        // Also cover the formal-first branch with no prior claim record.
        var formalFirst = await Json(await Activate(client, app, batch, "formal-first")); Bearer(client, formalFirst);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/api/admin/licenses/{batch.Licenses[0].Id}/device-policy/reset", new { reason = "测试新设备" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        await Error(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "formal-first" }), "TRIAL_UPGRADED");
    }

    [Fact] public async Task TrialAndFormalActivationShareAppAdmissionAndMalformedTrialConsumesGlobalAdmission()
    {
        using var client = fixture.CreateClient(); var code = "mixed-" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < 30; i++)
        {
            var response = i % 2 == 0 ? await client.PostAsJsonAsync("/api/client/trials/activate", new { AppCode = code.ToUpperInvariant() + " ", deviceId = "A" })
                : await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(code, "invalid", "A"));
            Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        var denied = await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = code, deviceId = "A" });
        await Error(denied, "RATE_LIMITED", HttpStatusCode.TooManyRequests); Assert.NotNull(denied.Headers.RetryAfter);
        await using var limited = fixture.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:GlobalPerMinute"] = "2" })));
        using var malformed = limited.CreateClient();
        for (var i = 0; i < 2; i++) await Error(await malformed.PostAsync("/api/client/trials/activate", new StringContent("[]", System.Text.Encoding.UTF8, "application/json")), "VALIDATION_ERROR", HttpStatusCode.BadRequest);
        await Error(await malformed.PostAsync("/api/client/trials/activate", new StringContent("[]", System.Text.Encoding.UTF8, "application/json")), "RATE_LIMITED", HttpStatusCode.TooManyRequests);
    }

    [Fact] public async Task PolicyOpenApiKeepsNamedContractsAndDocumentsCsrfAndPublicTrialActivation()
    {
        using var client = fixture.CreateClient(); var doc = await Json(await client.GetAsync("/openapi/v1.json")); var paths = doc.GetProperty("paths");
        var freeze = paths.GetProperty("/api/admin/licenses/{id}/freeze").GetProperty("post"); Assert.True(freeze.GetProperty("security")[0].TryGetProperty("AdminCookie", out _));
        Assert.Contains(freeze.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "X-CSRF-TOKEN");
        var put = paths.GetProperty("/api/admin/applications/{id}/trial-policy").GetProperty("put"); Assert.True(put.GetProperty("security")[0].TryGetProperty("CsrfCookie", out _));
        var trial = paths.GetProperty("/api/client/trials/activate").GetProperty("post"); Assert.False(trial.TryGetProperty("security", out _));
        var schemas = doc.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("TrialPolicyDto", out _)); Assert.True(schemas.TryGetProperty("LicenseFreezeDto", out _));
        Assert.True(schemas.GetProperty("ActivationDto").GetProperty("properties").TryGetProperty("licenseId", out _));
        Assert.True(schemas.GetProperty("HeartbeatDto").GetProperty("properties").TryGetProperty("features", out _));
    }

    [Fact] public async Task TrialClaimScopeIsPerAppDeviceAndNoPlaintextCredentialIsPersisted()
    {
        var (admin, app, _) = await fixture.Catalog(); using var adminDispose = admin; using var client = fixture.CreateClient();
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = new[] { " View ", "view" } }));
        var a = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "raw-device-A" }));
        var b = await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "raw-device-B" }));
        Assert.NotEqual(a.GetProperty("licenseId").GetGuid(), b.GetProperty("licenseId").GetGuid());
        Assert.Equal("view", Assert.Single(a.GetProperty("features").EnumerateArray()).GetString());
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var licenses = await db.Licenses.AsNoTracking().Where(x => x.ApplicationId == app.Id).ToArrayAsync(); Assert.Equal(2, licenses.Length);
        Assert.All(licenses, x => { Assert.Null(x.KeyDigest); Assert.Null(x.BatchId); Assert.Equal(1, x.MaxDevices); Assert.Equal(1, x.MaxConcurrentSessions); });
        var claims = await db.TrialClaims.AsNoTracking().Where(x => x.ApplicationId == app.Id).ToArrayAsync(); Assert.Equal(2, claims.Length);
        Assert.All(claims, x => Assert.DoesNotContain("raw-device", x.DeviceDigest));
        Assert.False(await db.Sessions.AnyAsync(x => x.TokenDigest == a.GetProperty("sessionToken").GetString()));
    }

    [Fact] public async Task FreezeAndClaimRecordsSurviveOrdinaryRetention()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient(); var id = batch.Licenses[0].Id;
        await Json(await Activate(client, app, batch, "formal"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "长期售后" })).StatusCode);
        await Json(await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() }));
        await Json(await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "trial-history" }));
        var old = fixture.Clock.GetUtcNow().AddDays(-200); var auditId = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.LicenseFreezes.SingleAsync(x => x.LicenseId == id)).FrozenAt = old;
            (await db.TrialClaims.SingleAsync(x => x.ApplicationId == app.Id)).ClaimedAt = old;
            db.AuditLogs.Add(new() { Id = auditId, ActorId = "retention-test", Action = "old", CreatedAt = old, TraceId = "test" });
            await db.SaveChangesAsync();
        }
        using var worker = new RetentionWorker(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Clock, fixture.Services.GetRequiredService<IConfiguration>(), NullLogger<RetentionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        for (var i = 0; i < 40; i++)
        {
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.AuditLogs.AnyAsync(x => x.Id == auditId)) break; await Task.Delay(25);
        }
        await worker.StopAsync(CancellationToken.None);
        using var final = fixture.Services.CreateScope(); var finalDb = final.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await finalDb.AuditLogs.AnyAsync(x => x.Id == auditId));
        Assert.True(await finalDb.LicenseFreezes.AnyAsync(x => x.LicenseId == id)); Assert.True(await finalDb.TrialClaims.AnyAsync(x => x.ApplicationId == app.Id));
    }

    [Fact] public async Task AbsoluteSessionExpiryReclaimsCapacityEvenIfLeaseStillLooksLive()
    {
        var (admin, app, _, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        await Json(await Activate(client, app, batch, "A")); var id = batch.Licenses[0].Id;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var farLease = fixture.Clock.GetUtcNow().AddDays(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Sessions\" SET \"LeaseExpiresAt\" = {farLease} WHERE \"BindingId\" IN (SELECT \"Id\" FROM \"Bindings\" WHERE \"LicenseId\" = {id})");
        }
        fixture.Clock.Advance(TimeSpan.FromHours(8));
        await Json(await Activate(client, app, batch, "B"));
    }

    [Fact] public async Task MixedRenewalReportsFrozenItemAndRenewsOtherCardsExactlyOnce()
    {
        var (admin, app, plan, batch) = await Card(); using var adminDispose = admin; using var client = fixture.CreateClient();
        var other = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id; var otherId = other.Licenses[0].Id;
        await Json(await Activate(client, app, batch, "A"));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{id}/freeze", new { reason = "暂停" })).StatusCode);
        var request = new LicenseOperationRequest(Guid.NewGuid(), [id, otherId], "extend", 3600, Reason: "批量续期");
        var first = await Json(await admin.PostAsJsonAsync("/api/admin/license-operations", request));
        Assert.Equal(1, first.GetProperty("succeeded").GetInt32()); Assert.Equal(1, first.GetProperty("failed").GetInt32());
        var failure = Assert.Single(first.GetProperty("items").EnumerateArray(), x => !x.GetProperty("success").GetBoolean()); Assert.Equal("LICENSE_FROZEN", failure.GetProperty("errorCode").GetString());
        var retry = await Json(await admin.PostAsJsonAsync("/api/admin/license-operations", request)); Assert.Equal(first.GetRawText(), retry.GetRawText());
        Assert.Equal(90000, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{otherId}"))!.License.DurationSeconds);
    }
}


