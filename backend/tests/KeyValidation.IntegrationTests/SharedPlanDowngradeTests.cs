using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanDowngradeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UnboundPlanRejectsDowngradeBeforeSchemaChanges()
    {
        using var admin = await fixture.Login();
        var created = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("无应用卡种", 3600));
        created.EnsureSuccessStatusCode();
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261009100000_AddApplicationPlanReferences"));
        Assert.Contains("SHARED_PLAN_DOWNGRADE_UNSUPPORTED", error.MessageText);
        Assert.Contains("20261009101000_GlobalizePlans",
            await db.Database.GetAppliedMigrationsAsync());
        var columns = await db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(*)::int AS "Value" FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = 'Plans'
                AND column_name = 'ApplicationId'
            """).SingleAsync();
        Assert.Equal(0, columns);
        await db.GetService<IMigrator>().MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
    }
}

public sealed class SharedPlanMultiReferenceDowngradeTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SharedPlanRejectsDowngradeWithoutDroppingEitherReference()
    {
        var (admin, a, _) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        var response = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("两应用共享", 3600));
        response.EnsureSuccessStatusCode();
        var plan = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        await SharedPlanTestData.Reference(fixture, a.Id, plan.Id);
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            db.GetService<IMigrator>().MigrateAsync("20261009100000_AddApplicationPlanReferences"));
        Assert.Contains("SHARED_PLAN_DOWNGRADE_UNSUPPORTED", error.MessageText);
        // EF has already reverted the newer naming migration before the guarded
        // historical downgrade fails. Inspect that historical schema, then upgrade.
        Assert.Equal(2, await db.Database.SqlQuery<int>($"""
            SELECT COUNT(*)::int AS "Value" FROM "ApplicationPlans" WHERE "PlanId" = {plan.Id}
            """).SingleAsync());
        Assert.Contains("20261009101000_GlobalizePlans",
            await db.Database.GetAppliedMigrationsAsync());
        await db.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(2, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
    }
}

