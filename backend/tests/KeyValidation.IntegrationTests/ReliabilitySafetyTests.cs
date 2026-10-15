using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilitySafetyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("risk")]
    [InlineData("notification")]
    public async Task QueuedWritesRecheckRevokedPermissionIncludingAnAlreadyReadNotification(string kind)
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        string userId, userName; Guid resource;
        using (var setup = fixture.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>(); var users = setup.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = new AdminUser { UserName = "queued-" + Guid.NewGuid().ToString("N") };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded); userId = user.Id; userName = user.UserName;
            if (kind == "risk")
            {
                var record = new RiskEvent { ApplicationId = app.Id, LicenseId = Guid.NewGuid(), Rule = "activation_burst", WindowStart = fixture.Clock.GetUtcNow().AddMinutes(-10), WindowEnd = fixture.Clock.GetUtcNow(), CreatedAt = fixture.Clock.GetUtcNow() };
                db.RiskEvents.Add(record); resource = record.Id;
            }
            else
            {
                var published = new OperationsEvent { ApplicationId = app.Id, EventType = "risk.detected", ResourceId = app.Id, DeduplicationKey = Guid.NewGuid().ToString("N"), CreatedAt = fixture.Clock.GetUtcNow() };
                var notification = new StaffNotification { ApplicationId = app.Id, EventId = published.Id, RecipientId = user.Id, ResourceId = app.Id, EventType = "risk.detected", CreatedAt = fixture.Clock.GetUtcNow(), ReadAt = fixture.Clock.GetUtcNow() };
                db.OperationsEvents.Add(published); db.StaffNotifications.Add(notification); resource = notification.Id;
            }
            await db.SaveChangesAsync();
        }
        // Establish the same narrow grant through the supported role adapter.
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{userId}/access", new UpdateAccountAccess(["risk.review"], false, [app.Id]))).StatusCode);
        using var actor = await fixture.Login(userName);
        using var block = fixture.Services.CreateScope(); var blockedDb = block.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await blockedDb.Database.BeginTransactionAsync();
        if (kind == "risk") await blockedDb.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_risk_events\" WHERE \"Id\" = {resource} FOR UPDATE");
        else await blockedDb.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_staff_notifications\" WHERE \"Id\" = {resource} FOR UPDATE");
        var pending = kind == "risk" ? actor.PostAsJsonAsync($"/api/admin/risk-events/{resource}/review", new ReviewRiskEvent("dismissed", "queued")) : actor.PostAsync($"/api/admin/notifications/{resource}/read", null);
        var waiting = false;
        try
        {
            var start = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(start) < TimeSpan.FromSeconds(10))
            {
                await blockedDb.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
                waiting = await blockedDb.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND cardinality(pg_blocking_pids(pid)) > 0) AS \"Value\"").SingleAsync();
                if (waiting) break; await Task.Delay(10);
            }
            if (waiting) Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{userId}/access", new UpdateAccountAccess([], false, [app.Id]))).StatusCode);
        }
        finally { await tx.CommitAsync(); }
        var response = await pending; Assert.True(waiting); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        if (kind == "risk") Assert.Equal("open", (await blockedDb.RiskEvents.AsNoTracking().SingleAsync(x => x.Id == resource)).Status);
    }

    [Fact]
    public async Task RetentionKeepsPendingDeliveryEventsAndOpenIncidents()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var old = fixture.Clock.GetUtcNow().AddDays(-200);
        var subscription = new WebhookSubscription { ApplicationId = app.Id, Name = "retention fixture", Url = "https://unused.example/events", ProtectedSecret = "unused", CreatedAt = old, UpdatedAt = old };
        var pending = new OperationsEvent { ApplicationId = app.Id, DeduplicationKey = Guid.NewGuid().ToString("N"), CreatedAt = old, DispatchedAt = old };
        var finished = new OperationsEvent { ApplicationId = app.Id, DeduplicationKey = Guid.NewGuid().ToString("N"), CreatedAt = old, DispatchedAt = old };
        db.WebhookSubscriptions.Add(subscription); db.OperationsEvents.AddRange(pending, finished);
        db.WebhookDeliveries.AddRange(new WebhookDelivery { ApplicationId = app.Id, EventId = pending.Id, SubscriptionId = subscription.Id, CreatedAt = old, ExpiresAt = fixture.Clock.GetUtcNow().AddHours(1), NextAttemptAt = fixture.Clock.GetUtcNow() },
            new WebhookDelivery { ApplicationId = app.Id, EventId = finished.Id, SubscriptionId = subscription.Id, CreatedAt = old, CompletedAt = old, Status = "succeeded", ExpiresAt = old.AddHours(24) });
        var open = new MonitoringIncident { Kind = "retention_open", StartedAt = old }; var resolved = new MonitoringIncident { Kind = "retention_closed", StartedAt = old, ResolvedAt = old };
        db.MonitoringIncidents.AddRange(open, resolved); await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<ReliabilityRetention>().CleanupAsync();
        Assert.True(await db.OperationsEvents.AnyAsync(x => x.Id == pending.Id)); Assert.True(await db.WebhookDeliveries.AnyAsync(x => x.EventId == pending.Id));
        Assert.False(await db.OperationsEvents.AnyAsync(x => x.Id == finished.Id)); Assert.False(await db.WebhookDeliveries.AnyAsync(x => x.EventId == finished.Id));
        Assert.True(await db.MonitoringIncidents.AnyAsync(x => x.Id == open.Id)); Assert.False(await db.MonitoringIncidents.AnyAsync(x => x.Id == resolved.Id));
    }
}
