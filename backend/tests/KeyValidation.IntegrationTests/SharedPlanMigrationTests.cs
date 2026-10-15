using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OldPlanIdsAndLicensesSurviveReferenceBackfill()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261009041239_ClientExtensions");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();

        var reference = await db.ApplicationPlans.SingleAsync(
            x => x.ApplicationId == app.Id && x.PlanId == plan.Id);
        Assert.True(reference.IsEnabled);
        Assert.Equal(plan.CreatedAt, reference.CreatedAt);
        Assert.Equal(plan.Id, (await db.Batches.SingleAsync(
            x => x.Id == issued.Batch.Id)).PlanId);
        var card = await db.Licenses.SingleAsync(x => x.Id == issued.Licenses[0].Id);
        Assert.Equal(plan.DurationSeconds, card.DurationSeconds);
        Assert.Equal(app.Id, card.ApplicationId);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
