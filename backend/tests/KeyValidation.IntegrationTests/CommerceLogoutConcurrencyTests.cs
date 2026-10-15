using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommerceLogoutConcurrencyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task LogoutWaitsForAlreadyRecheckedWriteWithoutUpgradingSharedLocks()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminCleanup = admin;
        var (portal, customer, license) = await Portal(admin, app.Id, plan.Id); using var portalCleanup = portal;
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION commerce_test_pause_portal_request() RETURNS trigger
            LANGUAGE plpgsql AS $test$
            BEGIN
                PERFORM pg_advisory_xact_lock(1262836066, 1);
                RETURN NEW;
            END $test$;
            CREATE TRIGGER commerce_test_pause_portal_request BEFORE INSERT ON "PortalRequests"
            FOR EACH ROW EXECUTE FUNCTION commerce_test_pause_portal_request();
            """);
        var requestId = Guid.NewGuid();
        Task<HttpResponseMessage>? write = null; Task<HttpResponseMessage>[] logouts = [];
        var writerReachedInsert = false; var logoutsWaitingExclusively = false; var revokedWhileWriteBlocked = false;
        try
        {
            await using (var blocker = await db.Database.BeginTransactionAsync())
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836066, 1)");
                write = portal.PostAsJsonAsync("/api/portal/requests", new { requestId, type = "renewal", licenseId = license });
                writerReachedInsert = await WaitFor(db, () => write.IsCompleted, """
                    SELECT EXISTS (
                        SELECT 1 FROM pg_locks writer JOIN pg_locks auth_lock ON auth_lock.pid = writer.pid
                        WHERE writer.locktype = 'advisory' AND writer.database = (SELECT oid FROM pg_database WHERE datname = current_database())
                            AND writer.classid = 1262836066 AND writer.objid = 1 AND writer.objsubid = 2 AND NOT writer.granted
                            AND auth_lock.locktype = 'advisory' AND auth_lock.classid = 1262836049
                            AND auth_lock.objid = 1 AND auth_lock.objsubid = 2
                            AND auth_lock.mode = 'ShareLock' AND auth_lock.granted
                    ) AS "Value"
                    """);
                if (writerReachedInsert)
                {
                    logouts = [portal.DeleteAsync("/api/portal/sessions"), portal.DeleteAsync("/api/portal/sessions")];
                    // Both logouts must wait for exclusive ownership before taking any shared recheck lock.
                    logoutsWaitingExclusively = await WaitFor(db, () => logouts.Any(x => x.IsCompleted), """
                        SELECT count(*) = 2 AS "Value" FROM pg_locks logout
                        WHERE logout.locktype = 'advisory' AND logout.database = (SELECT oid FROM pg_database WHERE datname = current_database())
                            AND logout.classid = 1262836049 AND logout.objid = 1 AND logout.objsubid = 2
                            AND logout.mode = 'ExclusiveLock' AND NOT logout.granted
                            AND NOT EXISTS (
                                SELECT 1 FROM pg_locks held WHERE held.pid = logout.pid AND held.locktype = 'advisory'
                                    AND held.classid = 1262836049 AND held.objid = 1 AND held.objsubid = 2
                                    AND held.mode = 'ShareLock' AND held.granted
                            )
                        """);
                    revokedWhileWriteBlocked = await db.PortalSessions.AsNoTracking().AnyAsync(x => x.CustomerId == customer && x.RevokedAt != null);
                }
                await blocker.CommitAsync();
            }
            using var response = await write!.WaitAsync(TimeSpan.FromSeconds(15));
            var logoutResponses = await Task.WhenAll(logouts).WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Assert.True(writerReachedInsert, "The request must be paused inside INSERT after its final shared authorization recheck.");
                Assert.True(logoutsWaitingExclusively, $"Logout must wait for the writer's commit, without acquiring a shared lock first. Session revoked before writer release: {revokedWhileWriteBlocked}.");
                Assert.False(revokedWhileWriteBlocked);
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                Assert.Equal(new[] { HttpStatusCode.NoContent, HttpStatusCode.Unauthorized }, logoutResponses.Select(x => x.StatusCode).Order());
                Assert.Equal(1, await db.PortalRequests.CountAsync(x => x.CustomerId == customer && x.RequestId == requestId));
                Assert.NotNull((await db.PortalSessions.AsNoTracking().SingleAsync(x => x.CustomerId == customer)).RevokedAt);
                Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
            }
            finally { foreach (var logout in logoutResponses) logout.Dispose(); }
        }
        finally
        {
            // The blocker transaction has ended before the trigger is removed, including failed assertions.
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER commerce_test_pause_portal_request ON \"PortalRequests\"; DROP FUNCTION commerce_test_pause_portal_request();");
        }
    }

    [Fact]
    public async Task LogoutThatCommitsFirstRejectsAnAlreadyAuthenticatedQueuedWrite()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminCleanup = admin;
        var (portal, customer, license) = await Portal(admin, app.Id, plan.Id); using var portalCleanup = portal;
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var requestId = Guid.NewGuid(); Task<HttpResponseMessage> write;
        bool writerWaiting, writeStillWaitingAfterLogout, revokedBeforeWriteRelease;
        HttpStatusCode logoutStatus;
        await using (var blocker = await db.Database.BeginTransactionAsync())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
            write = portal.PostAsJsonAsync("/api/portal/requests", new { requestId, type = "renewal", licenseId = license });
            writerWaiting = await WaitFor(db, () => write.IsCompleted, """
                SELECT EXISTS (
                    SELECT 1 FROM pg_stat_activity WHERE datname = current_database()
                        AND wait_event_type = 'Lock' AND query LIKE '%Applications%FOR SHARE%'
                ) AS "Value"
                """);
            using var logout = await portal.DeleteAsync("/api/portal/sessions").WaitAsync(TimeSpan.FromSeconds(15));
            logoutStatus = logout.StatusCode;
            writeStillWaitingAfterLogout = !write.IsCompleted;
            revokedBeforeWriteRelease = await db.PortalSessions.AsNoTracking().AnyAsync(x => x.CustomerId == customer && x.RevokedAt != null);
            await blocker.CommitAsync();
        }
        using var response = await write.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(writerWaiting, "The old session must authenticate and reach the application lock before logout.");
        Assert.Equal(HttpStatusCode.NoContent, logoutStatus); Assert.True(writeStillWaitingAfterLogout); Assert.True(revokedBeforeWriteRelease);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await db.PortalRequests.AnyAsync(x => x.CustomerId == customer));
        Assert.False(await db.AuditLogs.AnyAsync(x => x.ApplicationId == app.Id && x.Action == "portal-request.create"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await portal.GetAsync("/api/portal/me")).StatusCode);
    }

    private async Task<(HttpClient Portal, Guid Customer, Guid License)> Portal(HttpClient admin, Guid app, Guid plan)
    {
        var customer = CommerceTestData.Id(await CommerceTestData.Customer(admin, app));
        var order = CommerceTestData.Id(await CommerceTestData.Order(admin, app, plan, customer));
        var fulfilled = await CommerceTestData.Fulfill(admin, order);
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, customer);
        return (portal, customer, fulfilled.GetProperty("licenseIds")[0].GetGuid());
    }

    private static async Task<bool> WaitFor(AppDbContext db, Func<bool> completed, string predicateSql)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            if (completed()) return false;
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            if (await db.Database.SqlQueryRaw<bool>(predicateSql).SingleAsync()) return true;
            await Task.Delay(10);
        }
        return false;
    }
}
