using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed partial class OfflineLicenseTests
{
    [Fact]
    public async Task ConcurrentSigningLeavesExactlyOneActiveCertificateForTheBinding()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin; using var client = host.CreateClient();
        var replies = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Sign(admin, detail)));
        var signed = new List<System.Text.Json.JsonElement>(); foreach (var response in replies) signed.Add(await Json(response, HttpStatusCode.Created));
        var list = await Json(await admin.GetAsync($"/api/admin/offline-licenses?licenseId={detail.License.Id}"));
        Assert.Equal(6, list.GetProperty("total").GetInt32()); var active = Assert.Single(list.GetProperty("items").EnumerateArray(), x => x.GetProperty("status").GetString() == "active");
        foreach (var certificate in signed)
        {
            if (certificate.GetProperty("record").GetProperty("id").GetGuid() == active.GetProperty("id").GetGuid()) await Json(await Verify(client, certificate, app));
            else await Error(await Verify(client, certificate, app), "OFFLINE_LICENSE_SUPERSEDED");
        }
    }

    [Fact]
    public async Task OnlineVerificationRejectsTamperingWrongApplicationAndFutureCertificates()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin; using var client = host.CreateClient();
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created); var content = issued.GetProperty("licenseContent").GetString()!;
        await Error(await client.PostAsJsonAsync("/api/client/offline-licenses/verify", new { licenseContent = content, appCode = "wrong-application", deviceId = " raw-device " }), "OFFLINE_APPLICATION_MISMATCH");
        var parts = content.Split('.'); var signature = Decode(parts[2]); signature[0] ^= 1;
        parts[2] = Convert.ToBase64String(signature).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        await Error(await client.PostAsJsonAsync("/api/client/offline-licenses/verify", new { licenseContent = string.Join('.', parts), appCode = app.Code, deviceId = " raw-device " }), "OFFLINE_LICENSE_INVALID");
        fixture.Clock.Advance(TimeSpan.FromSeconds(-1));
        try { await Error(await Verify(client, issued, app), "OFFLINE_LICENSE_NOT_YET_VALID"); }
        finally { fixture.Clock.Advance(TimeSpan.FromSeconds(1)); }
    }

    [Fact]
    public async Task FreezeWaitsForSigningCommitThenOnlineVerificationRejectsTheCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin; using var client = host.CreateClient();
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION offline_test_pause_insert() RETURNS trigger LANGUAGE plpgsql AS $test$
            BEGIN PERFORM pg_advisory_xact_lock(1262836071, 1); RETURN NEW; END $test$;
            CREATE TRIGGER offline_test_pause_insert BEFORE INSERT ON "OfflineLicenses" FOR EACH ROW EXECUTE FUNCTION offline_test_pause_insert();
            """);
        Task<HttpResponseMessage>? signing = null; Task<HttpResponseMessage>? freeze = null; bool signingWaited = false, freezeWaited = false;
        try
        {
            await using (var blocker = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836071, 1)"); signing = Sign(admin, detail);
                signingWaited = await WaitForOfflineLock(db, () => signing.IsCompleted, """
                    SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND database = (SELECT oid FROM pg_database WHERE datname=current_database())
                        AND classid=1262836071 AND objid=1 AND objsubid=2 AND NOT granted) AS "Value"
                    """);
                if (signingWaited)
                {
                    freeze = admin.PostAsJsonAsync($"/api/admin/licenses/{detail.License.Id}/freeze", new { reason = "queued behind offline signing" });
                    freezeWaited = await WaitForOfflineLock(db, () => freeze.IsCompleted, """
                        SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%Licenses%FOR UPDATE%') AS "Value"
                        """);
                }
                await blocker.CommitAsync();
            }
            var issued = await Json(await signing!.WaitAsync(TimeSpan.FromSeconds(15)), HttpStatusCode.Created);
            Assert.True(signingWaited); Assert.True(freezeWaited, "License mutation must wait for the signing transaction's license lock.");
            Assert.Equal(HttpStatusCode.NoContent, (await freeze!.WaitAsync(TimeSpan.FromSeconds(15))).StatusCode);
            await Error(await Verify(client, issued, app), "LICENSE_FROZEN");
        }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER offline_test_pause_insert ON \"OfflineLicenses\"; DROP FUNCTION offline_test_pause_insert();"); }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task QueuedSigningAndIdempotentRevocationRecheckCurrentPermissions(bool revoke)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin;
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created); var id = issued.GetProperty("record").GetProperty("id").GetGuid();
        if (revoke) Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null)).StatusCode);
        var name = "offline-queued-" + Guid.NewGuid().ToString("N"); string accountId;
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>(); var user = new AdminUser { UserName = name };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded); accountId = user.Id;
        }
        using var actor = await Login(host, name); using var blockerScope = fixture.Services.CreateScope(); var db = blockerScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Task<HttpResponseMessage> pending; bool waiting;
        await using (var blocker = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
            pending = revoke ? actor.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null) : Sign(actor, detail);
            waiting = await WaitForOfflineLock(db, () => pending.IsCompleted, """
                SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event_type='Lock' AND query LIKE '%Applications%FOR SHARE%') AS "Value"
                """);
            await Json(await admin.PutAsJsonAsync($"/api/admin/accounts/{accountId}/access", new { allApplications = true, applicationIds = Array.Empty<Guid>(), permissions = new[] { "licenses.read" } }));
            await blocker.CommitAsync();
        }
        Assert.True(waiting); await Error(await pending.WaitAsync(TimeSpan.FromSeconds(15)), "UNAUTHENTICATED", HttpStatusCode.Unauthorized);
        Assert.Equal(1, await db.OfflineLicenses.CountAsync(x => x.LicenseId == detail.License.Id));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ActorId == accountId && x.Action.StartsWith("offline-license.")));
    }

    [Fact]
    public async Task ChannelAccountsCannotReadSignOrRevokeAnotherChannelsOfflineRecords()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, _, detail) = await Activated(host); using var owner = admin;
        var (_, name, _) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var (other, otherName, _) = await CommerceTestData.Channel(fixture, admin, app.Id); var otherId = CommerceTestData.Id(other);
        var customer = await CommerceTestData.Customer(admin, app.Id, otherId);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var license = await db.Licenses.SingleAsync(x => x.Id == detail.License.Id);
            license.AssignCustomer(CommerceTestData.Id(customer), otherId); await db.SaveChangesAsync();
        }
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created); var id = issued.GetProperty("record").GetProperty("id").GetGuid();
        using var actor = await Login(host, name);
        var list = await Json(await actor.GetAsync($"/api/admin/offline-licenses?applicationId={app.Id}")); Assert.Equal(0, list.GetProperty("total").GetInt32());
        await Error(await Sign(actor, detail), "NOT_FOUND", HttpStatusCode.NotFound);
        await Error(await actor.PostAsync($"/api/admin/offline-licenses/{id}/revoke", null), "NOT_FOUND", HttpStatusCode.NotFound);
        var logs = await Json(await actor.GetAsync($"/api/admin/audit-logs?targetId={id}")); Assert.Equal(0, logs.GetProperty("total").GetInt32());
        using var allowed = await Login(host, otherName);
        var visible = await Json(await allowed.GetAsync($"/api/admin/offline-licenses?applicationId={app.Id}")); Assert.Equal(1, visible.GetProperty("total").GetInt32());
        await Json(await Sign(allowed, detail), HttpStatusCode.Created);
    }

    [Fact]
    public async Task ReusableClientConsumesRealVersionGateAndTheServersOfflineSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); await using var host = Host(key);
        var (admin, app, batch, detail) = await Activated(host); using var owner = admin; using var http = host.CreateClient();
        var policy = $"/api/admin/applications/{app.Id}/version-policy";
        await Json(await admin.PutAsJsonAsync(policy, new { minimumVersion = "1.0.0", latestVersion = "2.0.0", enforceMinimum = false, updateUrl = "https://example.test/update.exe" }));
        var api = new KeyValidation.Client.LicenseApi(http, "1.0.0");
        var grant = await api.ActivateAsync(app.Code, batch.Licenses[0].Key, " raw-device ", "Reusable client", CancellationToken.None);
        Assert.True(grant.Version!.UpgradeAvailable); Assert.Equal("2.0.0", grant.Version.LatestVersion);
        var heartbeat = await api.HeartbeatAsync(grant.SessionToken!, CancellationToken.None); Assert.Equal(grant.AbsoluteExpiresAt, heartbeat.AbsoluteExpiresAt);
        await Json(await admin.PutAsJsonAsync(policy, new { minimumVersion = "2.0.0", latestVersion = "2.0.0", enforceMinimum = true }));
        var rejected = await Assert.ThrowsAsync<KeyValidation.Client.LicenseRejectedException>(() => api.HeartbeatAsync(grant.SessionToken!, CancellationToken.None));
        Assert.Equal("CLIENT_UPGRADE_REQUIRED", rejected.Message); Assert.Equal("2.0.0", rejected.Version!.MinimumVersion);
        var issued = await Json(await Sign(admin, detail), HttpStatusCode.Created);
        var offline = new KeyValidation.Client.OfflineLicenseVerifier(new Dictionary<string, string> { ["test-v1"] = key.ExportSubjectPublicKeyInfoPem() }, new TrustedTimeMemory(), fixture.Clock)
            .Open(issued.GetProperty("licenseContent").GetString()!, app.Code, " raw-device ", ["export.pdf"]);
        offline.Demand("export.pdf"); Assert.Equal(detail.License.Id, offline.LicenseId); Assert.Equal(detail.License.ExpiresAt, offline.ExpiresAt);
    }

    private sealed class TrustedTimeMemory : KeyValidation.Client.ITrustedTimeStore
    {
        private DateTimeOffset? latest;
        public DateTimeOffset? ReadLatest() => latest;
        public DateTimeOffset SaveLatest(DateTimeOffset value) { latest = latest > value ? latest : value; return latest.Value; }
    }

    private static async Task<bool> WaitForOfflineLock(AppDbContext db, Func<bool> completed, string sql)
    {
        var start = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(10))
        {
            if (completed()) return false;
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()"); if (await db.Database.SqlQueryRaw<bool>(sql).SingleAsync()) return true;
            await Task.Delay(10);
        }
        return false;
    }
}
