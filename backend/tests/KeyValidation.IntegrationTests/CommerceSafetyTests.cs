using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommerceSafetyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UserChosenOperationRequestIdCannotImpersonateAnotherChannelsEventOwner()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id); var channelId = CommerceTestData.Id(channel); await CommerceTestData.Price(admin, channelId, app.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, app.Id, channelId); var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer));
        var fulfilled = await CommerceTestData.Fulfill(admin, CommerceTestData.Id(order)); var channelBatchId = fulfilled.GetProperty("batchId").GetGuid();
        var legacy = await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(channelBatchId, [legacy.Licenses[0].Id], "metadata", Note: "legacy only", Reason: "scope regression"))).StatusCode);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var record = await db.OperationsEvents.SingleAsync(x => x.ResourceId == channelBatchId && x.EventType == "batch.completed"); Assert.Null(record.ChannelId);
    }

    [Fact]
    public async Task SameApplicationRisksInboxAndBatchEventCountsRemainChannelScoped()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channelA, nameA, userA) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var (channelB, _, userB) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var a = CommerceTestData.Id(channelA); var b = CommerceTestData.Id(channelB);
        await CommerceTestData.Price(admin, a, app.Id, plan.Id); await CommerceTestData.Price(admin, b, app.Id, plan.Id);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/notification-policy", new UpdateNotificationPolicy(true, [userA, userB], ["risk.detected", "order.fulfilled", "batch.completed"], [3, 7, 30]))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true, 1))).StatusCode);
        var licenseIds = new List<Guid>(); var orderIds = new List<Guid>();
        foreach (var channel in new[] { a, b })
        {
            var customer = await CommerceTestData.Customer(admin, app.Id, channel);
            var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer)); var orderId = CommerceTestData.Id(order); orderIds.Add(orderId);
            var done = await CommerceTestData.Fulfill(admin, orderId); licenseIds.Add(done.GetProperty("licenseIds")[0].GetGuid());
            var delivery = await admin.GetFromJsonAsync<OrderDeliveryDto>($"/api/admin/orders/{orderId}/delivery");
            using var client = fixture.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, delivery!.Licenses[0].Key, "device-" + channel))).StatusCode);
        }
        var legacy = await ApiFixture.Issue(admin, app, plan); licenseIds.Add(legacy.Licenses[0].Id);
        using var legacyClient = fixture.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await legacyClient.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, legacy.Licenses[0].Key, "legacy"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/license-operations", new LicenseOperationRequest(Guid.NewGuid(), licenseIds.ToArray(), "metadata", Note: "scoped operation", Reason: "scoped batch"))).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        using var actor = await fixture.Login(nameA);
        var risks = (await actor.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!;
        Assert.Equal(1, risks.Total); Assert.Equal(licenseIds[0], risks.Items[0].LicenseId);
        var inbox = await actor.GetFromJsonAsync<JsonElement>($"/api/admin/notifications?applicationId={app.Id}");
        Assert.Equal(3, inbox.GetProperty("total").GetInt32());
        foreach (var hidden in licenseIds.Skip(1).Concat(orderIds.Skip(1))) Assert.DoesNotContain(hidden.ToString(), inbox.GetRawText());
        var batch = Assert.Single(inbox.GetProperty("items").EnumerateArray(), x => x.GetProperty("eventType").GetString() == "batch.completed");
        Assert.Contains("成功 1", batch.GetProperty("message").GetString());
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hiddenRisk = await db.RiskEvents.SingleAsync(x => x.LicenseId == licenseIds[1]);
        var hiddenNotification = await db.StaffNotifications.FirstAsync(x => x.RecipientId == userB && x.ApplicationId == app.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/admin/risk-events/{hiddenRisk.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.PostAsJsonAsync($"/api/admin/risk-events/{hiddenRisk.Id}/review", new { status = "confirmed", reason = "hidden" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.PostAsync($"/api/admin/notifications/{hiddenNotification.Id}/read", null)).StatusCode);
        Assert.DoesNotContain(licenseIds[1].ToString(), await actor.GetStringAsync($"/api/admin/audit-logs?targetId={licenseIds[1]}"));
    }

    [Fact]
    public async Task FailureAtTransactionCommitRollsBackIssuanceQuotaDeliveryHistoryAndEvents()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 2); var channelId = CommerceTestData.Id(channel);
        await CommerceTestData.Price(admin, channelId, app.Id, plan.Id);
        var customer = await CommerceTestData.Customer(admin, app.Id, channelId); var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer), 2); var id = CommerceTestData.Id(order);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // A deferred PostgreSQL constraint runs only after all SaveChanges writes.
        // It proves the actual outer transaction can still undo every effect.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION commerce_test_commit_failure() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN IF NEW."Action" = 'fulfilled' THEN RAISE EXCEPTION 'TEST_COMMIT_FAILURE'; END IF; RETURN NEW; END $test$;
            CREATE CONSTRAINT TRIGGER commerce_test_commit_failure AFTER INSERT ON "t_order_histories" DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION commerce_test_commit_failure();
            """);
        try
        {
            Assert.Equal(HttpStatusCode.InternalServerError, (await admin.PostAsJsonAsync($"/api/admin/orders/{id}/fulfill", new { requestId = Guid.NewGuid() })).StatusCode);
            var pending = await db.Orders.AsNoTracking().SingleAsync(x => x.Id == id); Assert.Equal("pending", pending.Status); Assert.Null(pending.DeliveryCiphertext);
            Assert.Equal(0, await db.Licenses.CountAsync(x => x.ApplicationId == app.Id)); Assert.Equal(0, await db.Batches.CountAsync(x => x.ApplicationId == app.Id));
            Assert.Equal(2, (await db.Channels.AsNoTracking().SingleAsync(x => x.Id == channelId)).RemainingQuota);
            Assert.Equal(0, await db.ChannelQuotaEntries.CountAsync(x => x.OrderId == id)); Assert.Equal(0, await db.OrderHistories.CountAsync(x => x.OrderId == id && x.Action == "fulfilled"));
            Assert.Equal(0, await db.OperationsEvents.CountAsync(x => x.ResourceId == id));
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER commerce_test_commit_failure ON \"t_order_histories\"; DROP FUNCTION commerce_test_commit_failure();"); }
        Assert.Equal("fulfilled", (await CommerceTestData.Fulfill(admin, id)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task QuotaLedgerIsImmutableEvenThroughDirectDatabaseWrites()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var (channel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id, 5); var id = CommerceTestData.Id(channel);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"t_channel_quota_entries\" SET \"Delta\" = 999 WHERE \"ChannelId\" = {id}"));
        Assert.Equal(5, (await db.ChannelQuotaEntries.SingleAsync(x => x.ChannelId == id)).Delta);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedFulfillmentCannotCommitAfterStaffRevocationOrCredentialRotation(bool apiCredential)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var order = await CommerceTestData.Order(admin, app.Id, plan.Id, CommerceTestData.Id(customer)); var orderId = CommerceTestData.Id(order);
        HttpClient actor; string? userId = null; Guid? credentialId = null;
        if (apiCredential)
        {
            var secret = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Race", permissions = new[] { "orders.manage" } }), HttpStatusCode.Created);
            actor = fixture.CreateClient(); actor.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", secret.GetProperty("secret").GetString()); credentialId = secret.GetProperty("credential").GetProperty("id").GetGuid();
        }
        else
        {
            var role = await RbacTestData.Role(admin, ["orders.manage"]); var user = await RbacTestData.User(fixture); userId = user.Id; await RbacTestData.Assign(admin, user.Id, [role.Id]); actor = await fixture.Login(user.Name);
        }
        using var actorCleanup = actor;
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var blocker = new NpgsqlConnection(db.Database.GetConnectionString()); await blocker.OpenAsync(); await using var tx = await blocker.BeginTransactionAsync();
        await using (var command = new NpgsqlCommand("SELECT \"Id\" FROM \"t_applications\" WHERE \"Id\"=@id FOR UPDATE", blocker, tx)) { command.Parameters.AddWithValue("id", app.Id); await command.ExecuteNonQueryAsync(); }
        var prefix = apiCredential ? "/api/v1" : "/api/admin";
        var pending = actor.PostAsJsonAsync($"{prefix}/orders/{orderId}/fulfill", new { requestId = Guid.NewGuid() });
        try
        {
            await WaitForApplicationWaiter(db);
            if (apiCredential) Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/api-credentials/{credentialId}/rotate", null)).StatusCode);
            else { var empty = await RbacTestData.Role(admin, []); await RbacTestData.Assign(admin, userId!, [empty.Id]); }
        }
        finally { await tx.CommitAsync(); }
        Assert.Equal(HttpStatusCode.Unauthorized, (await pending).StatusCode);
        Assert.Equal("pending", (await db.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId)).Status); Assert.Equal(0, await db.Licenses.CountAsync(x => x.ApplicationId == app.Id));
    }

    [Fact]
    public async Task ExpiredDeliveryCleanupDeletesOnlyCiphertextAndNoSecretsReachOrdinaryPayloads()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = await CommerceTestData.Customer(admin, app.Id); var id = CommerceTestData.Id(customer);
        var order = await CommerceTestData.Order(admin, app.Id, plan.Id, id); var orderId = CommerceTestData.Id(order); await CommerceTestData.Fulfill(admin, orderId);
        var delivery = (await admin.GetFromJsonAsync<OrderDeliveryDto>($"/api/admin/orders/{orderId}/delivery"))!;
        var (portal, accessCode) = await CommerceTestData.Portal(fixture, admin, id); using var portalCleanup = portal;
        var token = portal.DefaultRequestHeaders.Authorization!.Parameter!;
        var credential = await CommerceTestData.Json(await admin.PostAsJsonAsync("/api/admin/api-credentials", new { applicationId = app.Id, name = "Safe secret", permissions = new[] { "licenses.read" } }), HttpStatusCode.Created);
        var apiSecret = credential.GetProperty("secret").GetString()!;
        var plain = await admin.GetStringAsync($"/api/admin/orders?applicationId={app.Id}") + await admin.GetStringAsync($"/api/admin/audit-logs?targetId={id}") + await admin.GetStringAsync("/api/admin/api-credentials");
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        plain += string.Join("", await db.AuditLogs.Where(x => x.ApplicationId == app.Id).Select(x => x.Changes).ToArrayAsync()) + string.Join("", await db.OperationsEvents.Where(x => x.ApplicationId == app.Id).Select(x => x.BodyJson).ToArrayAsync());
        foreach (var secret in new[] { delivery.Licenses[0].Key, accessCode, token, apiSecret, "private@example.test", "+86-private", "private-note" }) Assert.DoesNotContain(secret, plain);
        Assert.NotEqual(accessCode, (await db.Customers.SingleAsync(x => x.Id == id)).AccessCodeDigest);
        Assert.NotEqual(delivery.Licenses[0].Key, (await db.Orders.SingleAsync(x => x.Id == orderId)).DeliveryCiphertext);
        fixture.Clock.Advance(TimeSpan.FromDays(30)); await scope.ServiceProvider.GetRequiredService<ReliabilityRetention>().CleanupAsync(); db.ChangeTracker.Clear();
        var retained = await db.Orders.SingleAsync(x => x.Id == orderId); Assert.Null(retained.DeliveryCiphertext); Assert.Equal("fulfilled", retained.Status);
        Assert.Equal(1, await db.OrderLicenses.CountAsync(x => x.OrderId == orderId)); Assert.Equal(1, await db.Licenses.CountAsync(x => x.ApplicationId == app.Id));
    }

    private static async Task WaitForApplicationWaiter(AppDbContext db)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            if (await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE '%t_applications%FOR SHARE%'").SingleAsync() > 0) return;
            await Task.Delay(20);
        }
        Assert.Fail("Expected a real PostgreSQL application lock waiter.");
    }
}
