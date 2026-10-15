using System.Net;
using System.Net.Http.Json;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class OperationsAccessTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static readonly string[] OperatorPermissions = ["licenses.read", "licenses.issue", "licenses.manage", "reports.read", "audit.read", "risk.review", "customers.manage", "orders.manage"];

    [Fact]
    public async Task RestrictedResourcesDisappearBeforePagingCountingAndNestedLookups()
    {
        var (admin, visible, visiblePlan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, hidden, hiddenPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var first = await ApiFixture.Issue(admin, visible, visiblePlan);
        var other = await ApiFixture.Issue(admin, hidden, hiddenPlan);
        using var client = fixture.CreateClient();
        foreach (var (app, issued) in new[] { (visible, first), (hidden, other) })
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "scope-device"))).StatusCode);
        var otherId = other.Licenses[0].Id;
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{otherId}"))!;
        var binding = Assert.Single(detail.Bindings); var session = Assert.Single(binding.Sessions);
        var (id, name) = await CreateUser();
        await SetAccess(admin, id, [visible.Id], OperatorPermissions);
        using var restricted = await fixture.Login(name);

        var applications = (await restricted.GetFromJsonAsync<PagedResult<ApplicationDto>>("/api/admin/applications?pageSize=1"))!;
        Assert.Equal(1, applications.Total); Assert.Equal(visible.Id, Assert.Single(applications.Items).Id);
        foreach (var route in new[] { "licenses", "license-batches", "batch-overview", "expiring-licenses" })
        {
            var result = await restricted.GetFromJsonAsync<JsonElement>($"/api/admin/{route}?applicationId={hidden.Id}");
            Assert.Equal(0, result.GetProperty("total").GetInt32()); Assert.Empty(result.GetProperty("items").EnumerateArray());
        }
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync($"/api/admin/licenses/{otherId}")).StatusCode);
        foreach (var route in new[] { $"applications/{hidden.Id}/plans", $"applications/{hidden.Id}/trial-policy", $"batch-overview/{other.Batch.Id}", $"licenses/{otherId}/device-policy", $"licenses/{otherId}/freeze-history" })
            Assert.Equal(HttpStatusCode.NotFound, (await restricted.GetAsync("/api/admin/" + route)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.PostAsJsonAsync("/api/admin/licenses/search", new SearchLicense(other.Licenses[0].Key))).StatusCode);
        foreach (var route in new[] { $"licenses/{otherId}/disable", $"license-batches/{other.Batch.Id}/disable", $"device-bindings/{binding.Id}/release", $"sessions/{session.Id}/revoke" })
            Assert.Equal(HttpStatusCode.NotFound, (await restricted.PostAsync("/api/admin/" + route, null)).StatusCode);
        foreach (var target in new[] { hidden.Id, hiddenPlan.Id, other.Batch.Id, otherId, binding.Id, session.Id })
        {
            var audit = (await restricted.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={target}"))!;
            Assert.Equal(0, audit.Total);
        }
        Assert.NotEmpty((await restricted.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={first.Batch.Id}"))!.Items);
        var dashboard = (await restricted.GetFromJsonAsync<DashboardDto>("/api/admin/dashboard"))!;
        Assert.Equal(1, dashboard.TotalLicenses); Assert.Equal(1, dashboard.ActivatedLicenses); Assert.Equal(1, dashboard.OnlineSessions);
        // An administrative cookie on a software-client request must not constrain the software authorization.
        Assert.Equal(HttpStatusCode.OK, (await restricted.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(hidden.Code, other.Licenses[0].Key, "scope-device"))).StatusCode);
        var report = await restricted.GetFromJsonAsync<JsonElement>($"/api/admin/reports/operations?applicationId={hidden.Id}");
        Assert.Equal(0, report.GetProperty("totals").GetProperty("issued").GetInt32());
        Assert.Empty(report.GetProperty("breakdown").EnumerateArray());
    }

    [Fact]
    public async Task FinePermissionsDenyOperationsAndGrantChangesInvalidateExistingCookies()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        var (id, name) = await CreateUser();
        using var oldSession = await fixture.Login(name);
        await SetAccess(admin, id, [app.Id], ["licenses.read", "reports.read"]);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/admin/auth/access")).StatusCode);
        using var restricted = await fixture.Login(name);
        var access = await restricted.GetFromJsonAsync<JsonElement>("/api/admin/auth/access");
        Assert.False(access.GetProperty("allApplications").GetBoolean());
        Assert.Equal(app.Id, Assert.Single(access.GetProperty("applicationIds").EnumerateArray()).GetGuid());
        Assert.Equal(JsonValueKind.Null, access.GetProperty("channelId").ValueKind);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync("/api/admin/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsync($"/api/admin/licenses/{batch.Licenses[0].Id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.GetAsync("/api/admin/audit-logs")).StatusCode);
        await SetAccess(admin, id, [], []);
        using var empty = await fixture.Login(name);
        foreach (var route in new[] { "licenses", "dashboard", "reports/operations", "audit-logs" })
            Assert.Equal(HttpStatusCode.Forbidden, (await empty.GetAsync("/api/admin/" + route)).StatusCode);
    }

    [Fact]
    public async Task LegacyRolesKeepTheirDefaultsAndTheAccessAdapterWritesExplicitGrants()
    {
        using var admin = await fixture.Login();
        using var reader = await fixture.Login("readonly");
        var access = await reader.GetFromJsonAsync<JsonElement>("/api/admin/auth/access");
        Assert.True(access.GetProperty("allApplications").GetBoolean());
        Assert.Equal(new[] { "audit.read", "licenses.read", "reports.read" }, access.GetProperty("permissions").EnumerateArray().Select(x => x.GetString()).Order().ToArray());
        var (id, _) = await CreateUser("ReadOnly");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync($"/api/admin/accounts/{id}/access")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = true, applicationIds = Array.Empty<Guid>(), permissions = new[] { "licenses.read" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = true, applicationIds = Array.Empty<Guid>(), permissions = new[] { "unknown.permission" } })).StatusCode);
        // Standard RBAC intentionally supersedes the old hardcoded ReadOnly
        // ceiling. A platform manager can assign any catalog grant via the adapter.
        foreach (var permission in new[] { "licenses.issue", "integrations.manage" })
        {
            var changed = await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = true, applicationIds = Array.Empty<Guid>(), permissions = new[] { permission } });
            Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
            Assert.Equal(new[] { permission }, (await changed.Content.ReadFromJsonAsync<AccountAccessDto>())!.Permissions);
        }
        var me = (await admin.GetFromJsonAsync<UserDto>("/api/admin/auth/me"))!;
        var (otherAdmin, _) = await CreateUser("Admin");
        foreach (var target in new[] { me.Id, otherAdmin })
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/api/admin/accounts/{target}/access", new { allApplications = false, applicationIds = Array.Empty<Guid>(), permissions = new[] { "licenses.read" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = false, applicationIds = new[] { Guid.NewGuid() }, permissions = new[] { "licenses.read" } })).StatusCode);
        await SetAccess(admin, id, [], ["licenses.read"]);
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts/{id}/access")).GetProperty("applicationIds").EnumerateArray());
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = true, applicationIds = Array.Empty<Guid>(), permissions = new[] { "licenses.read" } })).StatusCode);
    }

    [Fact]
    public async Task MixedOperationsAndHistoricalReceiptsRedactResourcesAfterScopeShrinks()
    {
        var (admin, visible, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, hidden, otherPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var first = await ApiFixture.Issue(admin, visible, plan); var second = await ApiFixture.Issue(admin, hidden, otherPlan);
        var (id, name) = await CreateUser();
        await SetAccess(admin, id, [visible.Id, hidden.Id], OperatorPermissions);
        using var original = await fixture.Login(name);
        var request = new LicenseOperationRequest(Guid.NewGuid(), [first.Licenses[0].Id, second.Licenses[0].Id], "extend", 3600, Reason: "scope receipt");
        var initial = await original.PostAsJsonAsync("/api/admin/license-operations", request);
        Assert.Equal(2, (await initial.Content.ReadFromJsonAsync<OperationResult>())!.Succeeded);
        await SetAccess(admin, id, [visible.Id], OperatorPermissions);
        using var restricted = await fixture.Login(name);
        foreach (var response in new[] { await restricted.GetAsync($"/api/admin/license-operations/{request.RequestId}"), await restricted.PostAsJsonAsync("/api/admin/license-operations", request), await restricted.PostAsJsonAsync("/api/admin/license-operations", request with { RequestId = Guid.NewGuid() }) })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var result = (await response.Content.ReadFromJsonAsync<OperationResult>())!;
            Assert.Equal(1, result.Succeeded); Assert.Equal(1, result.Failed);
            var missing = result.Items.Single(x => x.LicenseId == second.Licenses[0].Id);
            Assert.False(missing.Success); Assert.Equal("NOT_FOUND", missing.ErrorCode); Assert.Null(missing.License);
            Assert.DoesNotContain(second.Licenses[0].KeyHint, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(90000, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{second.Licenses[0].Id}"))!.License.DurationSeconds);
        Assert.Equal(0, (await restricted.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={second.Licenses[0].Id}"))!.Total);
    }

    [Fact]
    public async Task AdvancedFiltersUseAndHalfOpenRangesAndHistoricalDeviceDigests()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var plan = (await (await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("week", 604800))).Content.ReadFromJsonAsync<PlanDto>())!;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 7, null, Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        var start = new DateTimeOffset(fixture.Clock.GetUtcNow().UtcDateTime.Date.AddDays(-3), TimeSpan.Zero);
        const string device = "never-return-this-device";
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            for (var i = 0; i < 7; i++)
            {
                var license = await db.Licenses.SingleAsync(x => x.Id == batch.Licenses[i].Id);
                license.CreatedAt = i == 1 ? start.AddDays(1) : start;
                license.Activate(i == 2 ? start.AddDays(2) : start.AddDays(1));
                if (i == 3) license.Extend(start.AddDays(1), 86400);
                license.Note = i == 4 ? "different note" : "customer alpha"; license.Tags = i == 5 ? ["other"] : ["review"];
                db.Bindings.Add(new() { LicenseId = license.Id, DeviceDigest = secrets.DigestDevice(app.Id, i == 6 ? "other device" : device), BoundAt = start.AddDays(1), ReleasedAt = start.AddDays(2) });
            }
            await db.SaveChangesAsync();
        }
        var response = await admin.PostAsJsonAsync("/api/admin/licenses/query", new
        {
            applicationId = app.Id, batchId = batch.Batch.Id, status = "active", page = 1, pageSize = 1,
            from = start, to = start.AddDays(1), activatedFrom = start.AddDays(1), activatedTo = start.AddDays(2),
            expiresFrom = start.AddDays(8), expiresTo = start.AddDays(9), note = "alpha", tag = "review", deviceId = device
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<PagedResult<LicenseDto>>())!;
        Assert.Equal(1, result.Total); Assert.Equal(batch.Licenses[0].Id, Assert.Single(result.Items).Id);
        Assert.DoesNotContain(device, await response.Content.ReadAsStringAsync());
        var audit = await admin.GetStringAsync("/api/admin/audit-logs"); Assert.DoesNotContain(device, audit);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/licenses/query", new { deviceId = device })).StatusCode);
        var (id, name) = await CreateUser(); await SetAccess(admin, id, [], ["licenses.read"]);
        using var restricted = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.NotFound, (await restricted.PostAsJsonAsync("/api/admin/licenses/query", new { applicationId = app.Id, deviceId = device })).StatusCode);
    }

    [Theory]
    [InlineData("from", "to")]
    [InlineData("activatedFrom", "activatedTo")]
    [InlineData("expiresFrom", "expiresTo")]
    public async Task AdvancedQueryRejectsReversedRanges(string lower, string upper)
    {
        using var admin = await fixture.Login(); var now = fixture.Clock.GetUtcNow();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/licenses/query", new Dictionary<string, object> { [lower] = now, [upper] = now.AddHours(-1) })).StatusCode);
    }

    [Fact]
    public async Task OperationsReportCountsLicenseFactsAndFirstActivationWithZeroFilledUtcDays()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 3, null, Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        var start = new DateTimeOffset(fixture.Clock.GetUtcNow().UtcDateTime.Date.AddDays(-2), TimeSpan.Zero);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Licenses.SingleAsync(x => x.Id == batch.Licenses[0].Id);
            first.CreatedAt = start; first.Activate(start.AddDays(1)); first.Activate(start.AddDays(1).AddHours(1));
            var frozen = await db.Licenses.SingleAsync(x => x.Id == batch.Licenses[1].Id);
            frozen.CreatedAt = start.AddDays(1); frozen.Activate(start.AddDays(1)); frozen.Freeze(start.AddDays(1).AddHours(1));
            (await db.Licenses.SingleAsync(x => x.Id == batch.Licenses[2].Id)).CreatedAt = start.AddDays(3);
            // Deliberately no audit or activation events: reporting must survive their retention cleanup.
            await db.AuditLogs.Where(x => x.TargetId == batch.Batch.Id).ExecuteDeleteAsync();
            await db.SaveChangesAsync();
        }
        var report = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/reports/operations?applicationId={app.Id}&from={Uri.EscapeDataString(start.ToString("O"))}&to={Uri.EscapeDataString(start.AddDays(3).ToString("O"))}");
        Assert.Equal("UTC", report.GetProperty("timeZone").GetString());
        var totals = report.GetProperty("totals"); Assert.Equal(2, totals.GetProperty("issued").GetInt32()); Assert.Equal(2, totals.GetProperty("activated").GetInt32()); Assert.Equal(1, totals.GetProperty("expired").GetInt32());
        var daily = report.GetProperty("daily").EnumerateArray().ToArray(); Assert.Equal(3, daily.Length);
        Assert.Equal(new[] { 1, 1, 0 }, daily.Select(x => x.GetProperty("issued").GetInt32()).ToArray());
        Assert.Equal(new[] { 0, 2, 0 }, daily.Select(x => x.GetProperty("activated").GetInt32()).ToArray());
        Assert.Equal(new[] { 0, 0, 1 }, daily.Select(x => x.GetProperty("expired").GetInt32()).ToArray());
        Assert.Equal(start.ToString("yyyy-MM-dd"), daily[0].GetProperty("date").GetString());
        var group = Assert.Single(report.GetProperty("breakdown").EnumerateArray());
        Assert.Equal(app.Id, group.GetProperty("applicationId").GetGuid()); Assert.Equal(plan.Id, group.GetProperty("planId").GetGuid());
        Assert.Equal(2, group.GetProperty("activated").GetInt32());
    }

    [Fact]
    public async Task OnlineReportsExcludeEveryInvalidSessionAndDeduplicateLicensesAtExactBoundaries()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, disabledApp, otherPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 8, null, Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        var disabledBatch = await ApiFixture.Issue(admin, disabledApp, otherPlan);
        var clockNow = fixture.Clock.GetUtcNow(); var now = new DateTimeOffset(clockNow.UtcTicks - clockNow.UtcTicks % 10, TimeSpan.Zero);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var secrets = scope.ServiceProvider.GetRequiredService<ISecretService>();
            var ids = batch.Licenses.Select(x => x.Id).Append(disabledBatch.Licenses[0].Id).ToArray();
            for (var i = 0; i < ids.Length; i++)
            {
                var license = await db.Licenses.SingleAsync(x => x.Id == ids[i]); license.Activate(i == 3 ? now.AddDays(-1) : now.AddHours(-1));
                if (i == 1) license.Disable(now); if (i == 2) license.Freeze(now);
                for (var j = 0; j < (i == 0 ? 2 : 1); j++)
                {
                    var binding = new DeviceBinding { LicenseId = license.Id, DeviceDigest = secrets.DigestDevice(license.ApplicationId, $"online-{i}-{j}"), BoundAt = now.AddMinutes(-1), ReleasedAt = i == 4 ? now : null };
                    db.Bindings.Add(binding);
                    var session = LicenseSession.Start(binding.Id, secrets.GenerateSession().Digest, now.AddSeconds(-30), license.ExpiresAt!.Value);
                    if (i == 5) session.Revoke(now);
                    db.Sessions.Add(session);
                    if (i == 6) db.Entry(session).Property(x => x.LeaseExpiresAt).CurrentValue = now;
                    if (i == 7) db.Entry(session).Property(x => x.AbsoluteExpiresAt).CurrentValue = now;
                }
            }
            (await db.Applications.SingleAsync(x => x.Id == disabledApp.Id)).DisabledAt = now;
            await db.SaveChangesAsync();
        }
        var report = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/reports/operations?applicationId={app.Id}");
        Assert.Equal(1, report.GetProperty("totals").GetProperty("onlineLicenses").GetInt32()); Assert.Equal(2, report.GetProperty("totals").GetProperty("onlineSessions").GetInt32());
        var other = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/reports/operations?applicationId={disabledApp.Id}");
        Assert.Equal(0, other.GetProperty("totals").GetProperty("onlineSessions").GetInt32());
    }

    [Fact]
    public async Task ReportsValidateRangeAndIncludeTrialsInTheirOwnGroup()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/trial-policy", new { isEnabled = true, durationSeconds = 3600, features = Array.Empty<string>() })).StatusCode);
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/trials/activate", new { appCode = app.Code, deviceId = "report trial" })).StatusCode);
        var report = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/reports/operations?applicationId={app.Id}");
        Assert.Equal(30, report.GetProperty("daily").GetArrayLength());
        var group = Assert.Single(report.GetProperty("breakdown").EnumerateArray()); Assert.Equal(JsonValueKind.Null, group.GetProperty("planId").ValueKind);
        Assert.Equal(1, group.GetProperty("activated").GetInt32()); Assert.Equal(1, group.GetProperty("onlineLicenses").GetInt32());
        var today = fixture.Clock.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/admin/reports/operations?from={today}&to=2020-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/reports/operations?from=2024-01-01&to=2026-01-01")).StatusCode);
    }

    [Fact]
    public async Task ReportOnlyAccountsCanFilterReportsUsingAnIsolatedCatalog()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, hidden, _) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var (id, name) = await CreateUser(); await SetAccess(admin, id, [app.Id], ["reports.read"]);
        using var reports = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await reports.GetAsync("/api/admin/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reports.GetAsync("/api/admin/licenses")).StatusCode);
        var response = await reports.GetAsync("/api/admin/reports/catalog");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var catalog = await response.Content.ReadFromJsonAsync<JsonElement>();
        var application = Assert.Single(catalog.GetProperty("applications").EnumerateArray());
        Assert.Equal(app.Id, application.GetProperty("id").GetGuid()); Assert.Equal(app.Name, application.GetProperty("name").GetString());
        Assert.Equal(2, application.EnumerateObject().Count());
        var catalogPlan = Assert.Single(catalog.GetProperty("plans").EnumerateArray());
        Assert.Equal(plan.Id, catalogPlan.GetProperty("id").GetGuid()); Assert.Equal(app.Id, catalogPlan.GetProperty("applicationId").GetGuid());
        Assert.Equal(plan.Name, catalogPlan.GetProperty("name").GetString()); Assert.Equal(4, catalogPlan.EnumerateObject().Count());
Assert.True(catalogPlan.GetProperty("isEnabled").GetBoolean());
        Assert.DoesNotContain(hidden.Id.ToString(), await response.Content.ReadAsStringAsync());
        await SetAccess(admin, id, [app.Id], ["licenses.read"]);
        using var licenses = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await licenses.GetAsync("/api/admin/reports/catalog")).StatusCode);
    }

    [Fact]
    public async Task QueuedLicenseWriteRechecksTheSecurityStampBeforeCommit()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (id, name) = await CreateUser(); await SetAccess(admin, id, [app.Id], OperatorPermissions);
        using var restricted = await fixture.Login(name);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
        var request = new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid());
        var pending = restricted.PostAsJsonAsync("/api/admin/license-batches", request);
        var waiting = await WaitForBlockedRequest(db);
        try { if (waiting) await SetAccess(admin, id, [], ["licenses.read"]); }
        finally { await tx.CommitAsync(); }
        var response = await pending;
        Assert.True(waiting);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await db.Batches.AnyAsync(x => x.RequestId == request.RequestId));
    }

    [Theory]
    [InlineData("batch")]
    [InlineData("operation")]
    public async Task QueuedReplaysCannotRevealPriorResultsAfterAuthorizationChanges(string kind)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (id, name) = await CreateUser(); await SetAccess(admin, id, [app.Id], OperatorPermissions);
        using var restricted = await fixture.Login(name);
        var request = new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid());
        var created = await restricted.PostAsJsonAsync("/api/admin/license-batches", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var batch = (await created.Content.ReadFromJsonAsync<IssuanceDto>())!;
        var operation = new LicenseOperationRequest(Guid.NewGuid(), [batch.Licenses[0].Id], "extend", 3600, Reason: "replay grant race");
        if (kind == "operation") Assert.Equal(HttpStatusCode.OK, (await restricted.PostAsJsonAsync("/api/admin/license-operations", operation)).StatusCode);
        var keyText = kind == "batch" ? id + ":" + request.RequestId : "license-operation:" + id + ":" + operation.RequestId;
        var advisoryKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(keyText)));
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({advisoryKey})");
        var pending = kind == "batch" ? restricted.PostAsJsonAsync("/api/admin/license-batches", request) : restricted.PostAsJsonAsync("/api/admin/license-operations", operation);
        var waiting = await WaitForBlockedRequest(db);
        try { if (waiting) await SetAccess(admin, id, [], ["licenses.read"]); }
        finally { await transaction.CommitAsync(); }
        var response = await pending;
        Assert.True(waiting); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("release")]
    [InlineData("unfreeze")]
    [InlineData("batch-disable")]
    [InlineData("application-enable")]
    public async Task NoChangeAdministrativeOperationsRecheckAuthorizationBeforeCommit(string operation)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var licenseId = batch.Licenses[0].Id;
        var targetId = licenseId;
        Func<HttpClient, Task<HttpResponseMessage>> send = client => client.PostAsync($"/api/admin/licenses/{licenseId}/enable", null);
        if (operation == "release")
        {
            using var software = fixture.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await software.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "no-change release"))).StatusCode);
            var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!;
            targetId = Assert.Single(detail.Bindings).Id;
            send = client => client.PostAsync($"/api/admin/device-bindings/{targetId}/release", null);
            Assert.Equal(HttpStatusCode.NoContent, (await send(admin)).StatusCode);
        }
        else if (operation == "unfreeze")
            send = client => client.PostAsJsonAsync($"/api/admin/licenses/{licenseId}/unfreeze", new FreezeLicense("already unfrozen"));
        else if (operation == "batch-disable")
        {
            targetId = batch.Batch.Id;
            send = client => client.PostAsync($"/api/admin/license-batches/{targetId}/disable", null);
            Assert.Equal(HttpStatusCode.NoContent, (await send(admin)).StatusCode);
        }
        else if (operation == "application-enable")
        {
            targetId = app.Id;
            send = client => client.PostAsync($"/api/admin/applications/{app.Id}/enable", null);
        }
        var isAdminOperation = operation == "application-enable";
        var (id, name) = await CreateUser(isAdminOperation ? "Admin" : "Operator");
        if (!isAdminOperation) await SetAccess(admin, id, [app.Id], OperatorPermissions);
        using var actor = await fixture.Login(name);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var auditCount = await db.AuditLogs.CountAsync(x => x.TargetId == targetId);
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
        var pending = send(actor);
        var waiting = await WaitForBlockedRequest(db);
        try
        {
            if (waiting)
            {
                if (isAdminOperation)
                    Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Operator" })).StatusCode);
                else await SetAccess(admin, id, [], ["licenses.read"]);
            }
        }
        finally { await transaction.CommitAsync(); }
        var response = await pending;
        Assert.True(waiting);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync(x => x.TargetId == targetId));
    }

    private static async Task<bool> WaitForBlockedRequest(AppDbContext db)
    {
        var waiting = 0; var deadline = DateTime.UtcNow.AddSeconds(10);
        while (waiting == 0 && DateTime.UtcNow < deadline)
        {
            // Statistics can be cached for the polling transaction. Refresh the
            // snapshot so connections which arrived after the first poll are visible.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_locks WHERE NOT granted AND pid IN (SELECT pid FROM pg_stat_activity WHERE datname = current_database())").SingleAsync();
            if (waiting == 0) await Task.Delay(20);
        }
        return waiting > 0;
    }

    private async Task<(string Id, string Name)> CreateUser(string role = "Operator")
    {
        using var scope = fixture.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = new AdminUser { UserName = "operations-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        return (user.Id, user.UserName);
    }

    private static async Task SetAccess(HttpClient admin, string id, Guid[] applications, string[] permissions)
    {
        var response = await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { permissions, allApplications = false, applicationIds = applications, channelId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
