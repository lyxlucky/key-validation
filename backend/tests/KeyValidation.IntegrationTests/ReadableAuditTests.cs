using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReadableAuditTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task BatchAuditKeepsThePlanLabelKnownAtIssuance()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Renamed later", plan.DurationSeconds, plan.MaxDevices, true))).StatusCode);
        var audit = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={batch.Batch.Id}")).GetProperty("items").EnumerateArray());
        Assert.Equal("batch", audit.GetProperty("targetType").GetString());
        Assert.Equal($"{plan.Name} × 1", audit.GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task BulkLicenseFailuresUseTheirRecordedOutcomeInsteadOfSuccessfulRequestStatus()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan); var license = issued.Licenses[0]; var missing = Guid.NewGuid();
        using var software = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await software.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, license.Key, "failure-test-device"))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/licenses/{license.Id}/freeze", new FreezeLicense("audit failure example"))).StatusCode);
        var response = await admin.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(Guid.NewGuid(), [license.Id, missing], "extend", 3600, Reason: "expect two recorded failures"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal(2, (await response.Content.ReadFromJsonAsync<OperationResult>())!.Failed);
        foreach (var target in new[] { license.Id, missing })
        {
            var log = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}&action=license.extend")).GetProperty("items").EnumerateArray());
            Assert.False(JsonSerializer.Deserialize<JsonElement>(log.GetProperty("changes").GetString()!).GetProperty("success").GetBoolean());
            Assert.Equal("failed", log.GetProperty("result").GetString());
        }
    }

    [Fact]
    public async Task LoginAndOperationCategoriesHaveDisjointServerSideTotalsAndFailureResults()
    {
        using var admin = await fixture.Login(); var (id, name) = await RbacTestData.User(fixture);
        using var attempt = fixture.CreateClient(); await ApiFixture.Csrf(attempt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await attempt.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, "wrong-password-never-log"))).StatusCode);
        using var actor = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.BadRequest, (await actor.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = "secret-do-not-record", newPassword = "ReplacementPassword1!" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await actor.PostAsync("/api/admin/auth/logout", null)).StatusCode);
        var all = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?actorId={id}&pageSize=100");
        var login = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/login-logs?actorId={id}&pageSize=1");
        var operation = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?actorId={id}&category=operation&pageSize=1");
        Assert.Equal(3, login.GetProperty("total").GetInt32()); Assert.Single(login.GetProperty("items").EnumerateArray());
        Assert.Equal(1, operation.GetProperty("total").GetInt32());
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        var filtered = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?actorId={id}&category=login&pageSize=100");
        var rows = filtered.GetProperty("items").EnumerateArray().ToArray();
        Assert.All(rows, row => { Assert.Equal("login", row.GetProperty("category").GetString()); Assert.Equal(name, row.GetProperty("actorName").GetString()); Assert.Equal(name, row.GetProperty("targetName").GetString()); Assert.Equal("account", row.GetProperty("targetType").GetString()); });
        var failed = Assert.Single(rows, row => row.GetProperty("result").GetString() == "failed");
        Assert.Equal("password", failed.GetProperty("method").GetString()); Assert.Equal("credentials", failed.GetProperty("errorCategory").GetString());
        Assert.Equal("failed", Assert.Single(operation.GetProperty("items").EnumerateArray()).GetProperty("result").GetString());
        Assert.Equal(3, (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/login-logs?actorName={name.ToUpperInvariant()}" )).GetProperty("total").GetInt32());
        foreach (var secret in new[] { fixture.Password, "wrong-password-never-log", "secret-do-not-record", "ReplacementPassword1!", "passwordHash", "securityStamp" }) Assert.DoesNotContain(secret, all.GetRawText());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/audit-logs?category=unrecognized")).StatusCode);
    }

    [Fact]
    public async Task LoginSourceUsesOnlyValidatedConnectionFacts()
    {
        using var admin = await fixture.Login(); var (id, name) = await RbacTestData.User(fixture);
        await using var proxied = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Proxy:KnownProxies:0"] = "10.20.30.40" })));
        foreach (var remote in new[] { "10.20.30.40", "10.20.30.99" })
        {
            var csrfContext = await proxied.Server.SendAsync(context => { context.Request.Method = "GET"; context.Request.Path = "/api/admin/auth/csrf"; });
            using var csrf = JsonDocument.Parse(await new StreamReader(csrfContext.Response.Body).ReadToEndAsync());
            var cookie = string.Join("; ", csrfContext.Response.Headers.SetCookie.Select(x => x!.Split(';')[0]));
            var body = JsonSerializer.SerializeToUtf8Bytes(new LoginRequest(name, "failed-source-attempt"));
            var response = await proxied.Server.SendAsync(context =>
            {
                context.Connection.RemoteIpAddress = IPAddress.Parse(remote); context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
                context.Request.Method = "POST"; context.Request.Path = "/api/admin/auth/login"; context.Request.ContentType = "application/json";
                context.Request.ContentLength = body.Length; context.Request.Body = new MemoryStream(body);
                context.Request.Headers.Cookie = cookie; context.Request.Headers["X-CSRF-TOKEN"] = csrf.RootElement.GetProperty("token").GetString();
                context.Request.Headers["X-Forwarded-For"] = "203.0.113.9"; context.Request.Headers["X-Real-IP"] = "198.51.100.88";
            });
            Assert.Equal(401, response.Response.StatusCode);
        }
        var logs = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/login-logs?actorId={id}");
        Assert.Equal(new[] { "10.20.30.99", "203.0.113.9" }, logs.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("sourceIp").GetString()).Order());
        Assert.DoesNotContain("198.51.100.88", logs.GetRawText());
    }

    [Fact]
    public async Task FailedLoginMfaAndSensitiveVerificationAreDifferentCategoriesAndNeverContainCodes()
    {
        using var admin = await fixture.Login(); var (id, name) = await RbacTestData.User(fixture);
        using var actor = await fixture.Login(name);
        var setup = await actor.PostAsJsonAsync("/api/admin/security/two-factor/setup", new { password = fixture.Password });
        var key = (await setup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sharedKey").GetString()!;
        var confirm = await actor.PostAsJsonAsync("/api/admin/security/two-factor/confirm", new { code = Code(key, fixture.Clock.GetUtcNow()) });
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var codes = (await confirm.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("recoveryCodes").EnumerateArray().Select(x => x.GetString()!).ToArray();
        await ApiFixture.Csrf(actor);
        Assert.Equal(HttpStatusCode.BadRequest, (await actor.PostAsJsonAsync("/api/admin/security/verify", new { code = "invalid-sensitive-code" })).StatusCode);
        using var login = fixture.CreateClient(); await ApiFixture.Csrf(login);
        var password = await login.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, fixture.Password));
        var challenge = (await password.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeToken").GetString()!;
        Assert.Equal(HttpStatusCode.BadRequest, (await login.PostAsJsonAsync("/api/admin/auth/two-factor", new { challengeToken = challenge, code = "invalid-login-code" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await login.PostAsJsonAsync("/api/admin/auth/two-factor", new { challengeToken = challenge, code = codes[0], useRecoveryCode = true })).StatusCode);
        var logs = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/login-logs?actorId={id}");
        Assert.Equal(3, logs.GetProperty("total").GetInt32()); // Initial password success, failed MFA, completed recovery login; no password challenge event.
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), x => x.GetProperty("result").GetString() == "failed" && x.GetProperty("method").GetString() == "totp");
        Assert.Contains(logs.GetProperty("items").EnumerateArray(), x => x.GetProperty("result").GetString() == "succeeded" && x.GetProperty("method").GetString() == "recovery-code");
        var operations = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?actorId={id}&category=operation");
        Assert.Contains(operations.GetProperty("items").EnumerateArray(), x => x.GetProperty("result").GetString() == "failed");
        foreach (var secret in codes.Append(key).Append(challenge).Append("invalid-sensitive-code").Append("invalid-login-code").Append(fixture.Password))
        { Assert.DoesNotContain(secret, logs.GetRawText()); Assert.DoesNotContain(secret, operations.GetRawText()); }
    }

    [Fact]
    public async Task AuditLabelsResolveVisibleTargetsAndNeverBypassScopeForAName()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (other, hidden, hiddenPlan) = await fixture.Catalog(); using var otherCleanup = other;
        var issued = await ApiFixture.Issue(admin, app, plan); var hiddenIssued = await ApiFixture.Issue(admin, hidden, hiddenPlan);
        using var software = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await software.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "audit-private-device"))).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{issued.Licenses[0].Id}"))!;
        var binding = Assert.Single(detail.Bindings); var session = Assert.Single(binding.Sessions);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/sessions/{session.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{binding.Id}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{issued.Licenses[0].Id}/disable", null)).StatusCode);
        var (id, name) = await RbacTestData.User(fixture);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { permissions = new[] { "audit.read" }, allApplications = false, applicationIds = new[] { app.Id } })).StatusCode);
        var misleading = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new() { Id = misleading, ActorId = id, Action = "license.disable", TargetId = hiddenIssued.Licenses[0].Id, ApplicationId = app.Id, CreatedAt = fixture.Clock.GetUtcNow(), TraceId = "legacy-mismatched-owner" });
            await db.SaveChangesAsync();
        }
        using var reader = await fixture.Login(name);
        foreach (var (target, type, label) in new[] { (app.Id, "application", app.Name), (plan.Id, "plan", plan.Name), (issued.Licenses[0].Id, "license", issued.Licenses[0].KeyHint), (binding.Id, "binding", issued.Licenses[0].KeyHint), (session.Id, "session", issued.Licenses[0].KeyHint) })
        {
            var result = await reader.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}");
            Assert.NotEmpty(result.GetProperty("items").EnumerateArray());
            Assert.All(result.GetProperty("items").EnumerateArray(), x => { Assert.Equal(type, x.GetProperty("targetType").GetString()); Assert.Equal(label, x.GetProperty("targetName").GetString()); Assert.Equal(app.Name, x.GetProperty("applicationName").GetString()); Assert.Equal(app.Id, x.GetProperty("applicationId").GetGuid()); });
        }
        var unknown = await reader.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={hiddenIssued.Licenses[0].Id}");
        Assert.Equal(JsonValueKind.Null, Assert.Single(unknown.GetProperty("items").EnumerateArray()).GetProperty("targetName").ValueKind);
        Assert.DoesNotContain(hiddenIssued.Licenses[0].KeyHint, unknown.GetRawText());
        Assert.Equal(0, (await reader.GetFromJsonAsync<JsonElement>("/api/admin/login-logs")).GetProperty("total").GetInt32());
        var text = await reader.GetStringAsync("/api/admin/audit-logs?pageSize=100");
        Assert.DoesNotContain(issued.Licenses[0].Key, text); Assert.DoesNotContain("audit-private-device", text);
    }

    [Fact]
    public async Task NewPolicyAndPlanAuditsCaptureSafeActualBeforeAndAfter()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Changed plan", 7200, 2, false))).StatusCode);
        var change = await LastChanges(admin, plan.Id, "plan.update");
        Assert.Equal(plan.Name, change.GetProperty("before").GetProperty("name").GetString()); Assert.Equal("Changed plan", change.GetProperty("after").GetProperty("name").GetString());
        Assert.Equal(86400, change.GetProperty("before").GetProperty("durationSeconds").GetInt64()); Assert.Equal(7200, change.GetProperty("after").GetProperty("durationSeconds").GetInt64());
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 1800, features = new[] { "demo" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true, 5))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/notification-policy", new UpdateNotificationPolicy(true, [], ["risk.detected"], [3]))).StatusCode);
        foreach (var action in new[] { "application.trial-policy.update", "risk-policy.update", "notification-policy.update" })
        { var facts = await LastChanges(admin, app.Id, action); Assert.True(facts.TryGetProperty("before", out _)); Assert.True(facts.TryGetProperty("after", out _)); }
        await using var webhookHost = fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?> { ["Webhooks:AllowedHosts:0"] = "example.com" })));
        using var webhookAdmin = webhookHost.CreateClient(); await ApiFixture.Csrf(webhookAdmin);
        Assert.Equal(HttpStatusCode.OK, (await webhookAdmin.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
        await ApiFixture.Csrf(webhookAdmin);
        var webhook = await webhookAdmin.PostAsJsonAsync("/api/admin/webhook-subscriptions", new CreateWebhookSubscription(app.Id, "Before hook", "https://example.com/hook?credential=never-audit-this", ["risk.detected"]));
        Assert.Equal(HttpStatusCode.Created, webhook.StatusCode);
        var hook = (await webhook.Content.ReadFromJsonAsync<WebhookSecretDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await webhookAdmin.PatchAsJsonAsync($"/api/admin/webhook-subscriptions/{hook.Subscription.Id}", new UpdateWebhookSubscription("After hook", "https://example.com/hook?credential=another-private-value", ["license.expiring"], false))).StatusCode);
        var hookChange = await LastChanges(admin, hook.Subscription.Id, "webhook.update");
        Assert.Equal("Before hook", hookChange.GetProperty("before").GetProperty("name").GetString()); Assert.Equal("After hook", hookChange.GetProperty("after").GetProperty("name").GetString());
        var logs = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={hook.Subscription.Id}");
        foreach (var secret in new[] { hook.Secret, "never-audit-this", "another-private-value" }) Assert.DoesNotContain(secret, logs);
    }

    [Fact]
    public async Task HistoricalLoginSourceRemainsUnknownAndBackupRequestMeansSubmitted()
    {
        using var admin = await fixture.Login(); var actor = (await admin.GetFromJsonAsync<UserDto>("/api/admin/auth/me"))!;
        var historical = Guid.NewGuid(); var backup = new BackupJob { Status = "failed", CreatedAt = fixture.Clock.GetUtcNow(), ErrorCode = "BACKUP_FAILED" };
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.BackupJobs.Add(backup);
            db.AuditLogs.Add(new() { Id = historical, ActorId = actor.Id, Action = "security.login", TargetId = Guid.Parse(actor.Id), CreatedAt = fixture.Clock.GetUtcNow(), Changes = "{}", TraceId = "historical" });
            db.AuditLogs.Add(new() { ActorId = actor.Id, Action = "backup.create", TargetId = backup.Id, CreatedAt = fixture.Clock.GetUtcNow(), Changes = "{}", TraceId = "submitted" });
            await db.SaveChangesAsync();
        }
        var login = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/login-logs?actorId={actor.Id}");
        Assert.Equal(JsonValueKind.Null, Assert.Single(login.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == historical).GetProperty("sourceIp").ValueKind);
        var request = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={backup.Id}")).GetProperty("items").EnumerateArray());
        Assert.Equal("submitted", request.GetProperty("result").GetString()); Assert.Equal("backup", request.GetProperty("targetType").GetString());
    }

    private static async Task<JsonElement> LastChanges(HttpClient admin, Guid target, string action)
    {
        var result = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}&action={action}");
        return JsonSerializer.Deserialize<JsonElement>(Assert.Single(result.GetProperty("items").EnumerateArray()).GetProperty("changes").GetString()!);
    }
    private sealed class HasBody : IHttpRequestBodyDetectionFeature { public bool CanHaveBody => true; }
    private static string Code(string key, DateTimeOffset now)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = string.Concat(key.Select(c => Convert.ToString(alphabet.IndexOf(c), 2).PadLeft(5, '0')));
        var bytes = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();
        var counter = new byte[8]; BinaryPrimitives.WriteInt64BigEndian(counter, now.ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(bytes, counter); var offset = hash[^1] & 15;
        return ((BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
