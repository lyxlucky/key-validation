using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class TableNamingMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task RenameRoundTripPreservesLicensesReferencesAndIdentity()
    {
        var (admin, application, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var issuance = await ApiFixture.Issue(admin, application, plan);
        var licenseId = issuance.Licenses[0].Id;
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        var accountIds = await db.Users.AsNoTracking().Select(user => user.Id).Order().ToArrayAsync();

        await migrator.MigrateAsync("20261009101000_GlobalizePlans");
        Assert.Equal(1, await db.Database.SqlQuery<int>(
            $"""SELECT COUNT(*)::int AS "Value" FROM "Licenses" WHERE "Id" = {licenseId}""").SingleAsync());
        Assert.Equal(accountIds.Length, await db.Database.SqlQueryRaw<int>(
            """SELECT COUNT(*)::int AS "Value" FROM "AspNetUsers" """).SingleAsync());

        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.False(db.Database.HasPendingModelChanges());
        var license = await db.Licenses.AsNoTracking().SingleAsync(value => value.Id == licenseId);
        Assert.Equal(application.Id, license.ApplicationId);
        Assert.Equal(plan.Id, (await db.Batches.AsNoTracking().SingleAsync(value => value.Id == issuance.Batch.Id)).PlanId);
        Assert.True(await db.ApplicationPlans.AnyAsync(value => value.ApplicationId == application.Id && value.PlanId == plan.Id));
        Assert.Equal(accountIds, await db.Users.AsNoTracking().Select(user => user.Id).Order().ToArrayAsync());
        using var signedIn = await fixture.Login();
        Assert.NotNull(await signedIn.GetFromJsonAsync<UserDto>("/api/admin/auth/me"));
    }

    [Fact]
    public async Task BusinessAndIdentityTablesUseThePrefixWithoutChangingMigrationHistory()
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tables = await db.Database.SqlQueryRaw<string>("""
            SELECT table_name AS "Value" FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """).ToArrayAsync();
        Assert.Contains("__EFMigrationsHistory", tables);
        var applicationTables = tables.Where(table => table != "__EFMigrationsHistory").ToArray();
        Assert.Equal(47, applicationTables.Length);
        Assert.All(applicationTables, table => Assert.Matches("^t_[a-z][a-z0-9_]*$", table));
        Assert.Contains("t_applications", tables);
        Assert.Contains("t_asp_net_users", tables);
        Assert.Contains("t_order_licenses", tables);
        Assert.DoesNotContain("Applications", tables);
    }
}
