using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanConcurrencyTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task IssuanceWaitingOnReferenceLockSeesCommittedPause()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "PlanId" FROM "ApplicationPlans" WHERE "ApplicationId" = {app.Id} AND "PlanId" = {plan.Id} FOR UPDATE""");
        var pending = admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()));
        var waiting = 0;
        for (var attempt = 0; attempt < 100 && waiting == 0; attempt++)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            waiting = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*)::int AS "Value" FROM pg_stat_activity
                WHERE datname = current_database() AND wait_event_type = 'Lock'
                AND query LIKE '%ApplicationPlans%'
                """).SingleAsync();
            if (waiting == 0) await Task.Delay(20);
        }
        Assert.True(waiting > 0, "Expected issuance to wait on the application reference lock.");
        var reference = await db.ApplicationPlans.SingleAsync(
            x => x.ApplicationId == app.Id && x.PlanId == plan.Id);
        reference.IsEnabled = false;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await pending).StatusCode);
        Assert.Equal(0, await db.Batches.CountAsync(x => x.ApplicationId == app.Id));
    }

    [Fact]
    public async Task ConcurrentReferencePutsCreateOneRowAndOneBindAudit()
    {
        var (admin, app, _) = await fixture.Catalog();
        using var cleanup = admin;
        var created = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("并发引用", 3600));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var plan = (await created.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            admin.PutAsJsonAsync(
                $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
                new SetPlanReference(true))));
        Assert.All(responses, x => Assert.Equal(HttpStatusCode.OK, x.StatusCode));
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
        Assert.Equal(1, await db.AuditLogs.CountAsync(
            x => x.TargetId == plan.Id && x.Action == "application-plan.bind"));
    }
}
