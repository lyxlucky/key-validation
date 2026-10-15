using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public class ManagementTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    private static LicenseOperationRequest Operation(Guid[] ids, string action = "extend", long? seconds = 3600) => new(Guid.NewGuid(), ids, action, seconds, Reason: "customer renewal");
    private static async Task<OperationResult> Submit(HttpClient client, LicenseOperationRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/admin/license-operations", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<OperationResult>())!;
    }

    [Fact] public async Task ConcurrentAndRepeatedExtensionRunsOnceAndReceiptIsActorScoped()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var request = Operation([id]);
        var results = await Task.WhenAll(Submit(admin, request), Submit(admin, request));
        Assert.Equal(results[0].CompletedAt, results[1].CompletedAt);
        Assert.Equal(90000, Assert.Single(results[0].Items).License!.DurationSeconds);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        Assert.Equal(90000, detail.License.DurationSeconds);
        var receipt = (await admin.GetFromJsonAsync<OperationResult>($"/api/admin/license-operations/{request.RequestId}"))!;
        Assert.Equal(results[0].CompletedAt, receipt.CompletedAt);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/license-operations", request with { AdditionalSeconds = 7200 })).StatusCode);
        using var other = await fixture.Login("operator");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/admin/license-operations/{request.RequestId}")).StatusCode);
        var audit = (await admin.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={id}&action=license.extend"))!;
        Assert.Single(audit.Items);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.OperationReceipts.SingleAsync(x => x.RequestId == request.RequestId);
        Assert.DoesNotContain(batch.Licenses[0].Key, stored.ResultJson);
        Assert.DoesNotContain("digest", stored.ResultJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task MissingAndOverLimitItemsFailWithoutCancellingValidItems()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var first = await ApiFixture.Issue(admin, app, plan);
        var maximumPlan = (await (await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("maximum", 315360000))).Content.ReadFromJsonAsync<PlanDto>())!;
        var maximum = await ApiFixture.Issue(admin, app, maximumPlan); var missing = Guid.NewGuid();
        var result = await Submit(admin, Operation([first.Licenses[0].Id, maximum.Licenses[0].Id, missing]));
        Assert.Equal(1, result.Succeeded); Assert.Equal(2, result.Failed);
        Assert.Equal("NOT_FOUND", result.Items.Single(x => x.LicenseId == missing).ErrorCode);
        Assert.Equal("DURATION_LIMIT_EXCEEDED", result.Items.Single(x => x.LicenseId == maximum.Licenses[0].Id).ErrorCode);
        Assert.Equal(90000, result.Items.Single(x => x.Success).License!.DurationSeconds);
        Assert.Equal(315360000, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{maximum.Licenses[0].Id}"))!.License.DurationSeconds);
    }

    [Fact] public async Task UnexpectedDatabaseFailureRollsBackLicensesAuditsAndReceipt()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 2, null, Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        var ids = batch.Licenses.Select(x => x.Id).ToArray(); var request = Operation(ids);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION management_test_reject_receipt() RETURNS trigger LANGUAGE plpgsql AS $body$
            BEGIN RAISE EXCEPTION 'intentional integration test failure'; END; $body$;
            CREATE TRIGGER management_test_reject_receipt BEFORE INSERT ON "t_operation_receipts"
            FOR EACH ROW EXECUTE FUNCTION management_test_reject_receipt();
            """);
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync("/api/admin/license-operations", request)).StatusCode);
            Assert.All(await db.Licenses.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(), license => Assert.Equal(86400, license.DurationSeconds));
            Assert.False(await db.OperationReceipts.AnyAsync(x => x.RequestId == request.RequestId));
            Assert.False(await db.AuditLogs.AnyAsync(x => ids.Contains(x.TargetId) && x.Action == "license.extend"));
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER management_test_reject_receipt ON "t_operation_receipts";
                DROP FUNCTION management_test_reject_receipt();
                """);
        }
        Assert.Equal(2, (await Submit(admin, request)).Succeeded);
    }

    [Fact] public async Task MetadataIsNormalizedSearchableAndReplayFingerprintUsesNormalizedValues()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var request = Operation([id], "metadata", null) with { Note = "  客户备注 <script>  ", Tags = [" paid ", "urgent", "paid"], Reason = " annotate " };
        var first = await Submit(admin, request);
        Assert.Equal("客户备注 <script>", first.Items[0].License!.Note);
        Assert.Equal(["paid", "urgent"], first.Items[0].License!.Tags);
        var retry = await Submit(admin, request with { Note = "客户备注 <script>", Tags = ["urgent", "paid"], Reason = "annotate" });
        Assert.Equal(first.CompletedAt, retry.CompletedAt);
        var found = (await admin.GetFromJsonAsync<PagedResult<LicenseDto>>($"/api/admin/licenses?applicationId={app.Id}&note={Uri.EscapeDataString("客户备注")}&tag=paid"))!;
        Assert.Equal(id, Assert.Single(found.Items).Id);
        Assert.Empty((await admin.GetFromJsonAsync<PagedResult<LicenseDto>>($"/api/admin/licenses?applicationId={app.Id}&tag=aid"))!.Items);
        var clear = await Submit(admin, Operation([id], "metadata", null) with { Note = "", Tags = [] });
        Assert.Null(clear.Items[0].License!.Note); Assert.Empty(clear.Items[0].License!.Tags);
    }

    [Fact] public async Task InvalidPayloadsAndReadOnlyCannotMutateOrCreateReceipts()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        var valid = Operation([id]);
        var invalid = new[] { valid with { LicenseIds = null! }, valid with { LicenseIds = [] }, valid with { LicenseIds = [id, id] }, valid with { LicenseIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() }, valid with { Reason = null! }, valid with { Action = null! }, valid with { AdditionalSeconds = long.MaxValue }, valid with { AdditionalSeconds = 3601 }, valid with { Reason = new string('x', 501) }, valid with { Action = "metadata", AdditionalSeconds = null, Tags = [null!] } };
        foreach (var request in invalid)
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/license-operations", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/admin/license-operations/{valid.RequestId}")).StatusCode);
        using var reader = await fixture.Login("readonly");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/admin/license-operations", valid)).StatusCode);
        Assert.Equal(86400, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!.License.DurationSeconds);
    }

    [Fact] public async Task DisableRevokesImmediatelyAndEnableAndRenewalNeverReviveSessions()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        using var client = fixture.CreateClient();
        var activated = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"))).Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", activated.SessionToken);
        await Submit(admin, Operation([id], "disable", null));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        await Submit(admin, Operation([id]));
        Assert.Equal("disabled", (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!.License.Status);
        await Submit(admin, Operation([id], "enable", null));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!;
        Assert.Null(Assert.Single(detail.Bindings).ReleasedAt);
        Assert.NotNull(Assert.Single(detail.Bindings[0].Sessions).RevokedAt);
    }

    [Fact] public async Task ActiveSessionGetsNewLicenseExpiryWithoutExtendingSessionAbsoluteLimit()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        using var client = fixture.CreateClient();
        var activated = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"))).Content.ReadFromJsonAsync<ActivationDto>())!;
        await Submit(admin, Operation([batch.Licenses[0].Id]));
        client.DefaultRequestHeaders.Authorization = new("Bearer", activated.SessionToken);
        var heartbeat = (await (await client.PostAsync("/api/client/sessions/heartbeat", null)).Content.ReadFromJsonAsync<HeartbeatDto>())!;
        Assert.Equal(activated.ExpiresAt.AddHours(1), heartbeat.ExpiresAt);
        Assert.Equal(activated.AbsoluteExpiresAt, heartbeat.AbsoluteExpiresAt);
    }

    [Fact] public async Task ExpiredRenewalRequiresNewSessionAndPreservesActivationAndBinding()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var id = batch.Licenses[0].Id;
        using var client = fixture.CreateClient(); var activation = new ActivateLicense(app.Code, batch.Licenses[0].Key, "device");
        var first = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", activation)).Content.ReadFromJsonAsync<ActivationDto>())!;
        fixture.Clock.Advance(TimeSpan.FromDays(2));
        using var freshAdmin = await fixture.Login();
        var result = await Submit(freshAdmin, Operation([id]));
        var renewed = Assert.Single(result.Items).License!;
        Assert.Equal(first.ServerTime, renewed.ActivatedAt);
        Assert.Equal(result.CompletedAt.AddHours(1), renewed.ExpiresAt);
        Assert.Equal(86400, renewed.DurationSeconds);
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        var second = (await client.PostAsJsonAsync("/api/client/licenses/activate", activation));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(renewed.ExpiresAt, (await second.Content.ReadFromJsonAsync<ActivationDto>())!.ExpiresAt);
        Assert.Single((await freshAdmin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!.Bindings);
    }

    [Fact] public async Task CrossApplicationOperationsUseStableLockOrderAndCanonicalIdFingerprint()
    {
        var (admin, firstApp, firstPlan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, secondApp, secondPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var first = await ApiFixture.Issue(admin, firstApp, firstPlan); var second = await ApiFixture.Issue(admin, secondApp, secondPlan);
        var ids = new[] { first.Licenses[0].Id, second.Licenses[0].Id };
        var request = Operation(ids);
        var results = await Task.WhenAll(Submit(admin, request), Submit(otherAdmin, Operation(ids.Reverse().ToArray())));
        Assert.All(results, result => Assert.Equal(2, result.Succeeded));
        var retry = await Submit(admin, request with { LicenseIds = ids.Reverse().ToArray() });
        Assert.Equal(results[0].CompletedAt, retry.CompletedAt);
        foreach (var id in ids) Assert.Equal(93600, (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{id}"))!.License.DurationSeconds);
    }

    [Fact] public async Task BatchOverviewCountsActivationAndDisableIndependentlyAndFilters()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 2, "campaign 2026", Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"))).StatusCode);
        await Submit(admin, Operation([batch.Licenses[0].Id], "disable", null));
        var overview = (await admin.GetFromJsonAsync<BatchOverviewDto>($"/api/admin/batch-overview/{batch.Batch.Id}"))!;
        Assert.Equal(2, overview.Quantity); Assert.Equal(1, overview.ActivatedCount); Assert.Equal(1, overview.DisabledCount);
        Assert.Equal(app.Name, overview.ApplicationName); Assert.Equal(plan.Name, overview.PlanName); Assert.Equal("admin", overview.CreatedByName);
        var found = (await admin.GetFromJsonAsync<PagedResult<BatchOverviewDto>>($"/api/admin/batch-overview?applicationId={app.Id}&planId={plan.Id}&createdBy={overview.CreatedBy}&search=campaign"))!;
        Assert.Equal(overview.Id, Assert.Single(found.Items).Id);
        Assert.Empty((await admin.GetFromJsonAsync<PagedResult<BatchOverviewDto>>($"/api/admin/batch-overview?applicationId={app.Id}&search=missing"))!.Items);
    }

    [Fact] public async Task ExpiringQueryUsesExclusiveNowInclusiveWindowAndExcludesUnavailableLicenses()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = (await (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 6, null, Guid.NewGuid()))).Content.ReadFromJsonAsync<IssuanceDto>())!;
        var now = fixture.Clock.GetUtcNow(); now = new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = batch.Licenses.Select(x => x.Id).ToArray();
            var rows = await db.Licenses.Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
            rows[ids[0]].Activate(now.AddDays(-1)); // expiry = now, excluded
            rows[ids[1]].Activate(now.AddHours(-23)); // expiry = now + 1 hour
            rows[ids[2]].Activate(now.AddDays(2)); // expiry = three-day upper boundary, included
            rows[ids[3]].Activate(now.AddDays(2).AddSeconds(1)); // beyond upper boundary
            rows[ids[4]].Activate(now); rows[ids[4]].Disable(now);
            // ids[5] remains unused
            await db.SaveChangesAsync();
        }
        var result = (await admin.GetFromJsonAsync<ExpiringLicensesDto>($"/api/admin/expiring-licenses?applicationId={app.Id}&withinDays=3"))!;
        Assert.Equal(now, result.ServerTime); Assert.Equal(3, result.WithinDays);
        Assert.Equal([batch.Licenses[1].Id, batch.Licenses[2].Id], result.Items.Select(x => x.Id)); Assert.Equal(2, result.Total);
        foreach (var invalidDays in new[] { 1, 2 })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/admin/expiring-licenses?withinDays={invalidDays}")).StatusCode);
        foreach (var validDays in new[] { 7, 30 })
        {
            var wider = (await admin.GetFromJsonAsync<ExpiringLicensesDto>($"/api/admin/expiring-licenses?applicationId={app.Id}&withinDays={validDays}"))!;
            Assert.Equal(validDays, wider.WithinDays); Assert.Equal(3, wider.Total);
        }
        await admin.PostAsync($"/api/admin/applications/{app.Id}/disable", null);
        Assert.Empty((await admin.GetFromJsonAsync<ExpiringLicensesDto>($"/api/admin/expiring-licenses?applicationId={app.Id}&withinDays=7"))!.Items);
    }
}
