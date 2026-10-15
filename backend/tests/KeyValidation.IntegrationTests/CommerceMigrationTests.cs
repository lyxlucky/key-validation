using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class CommerceMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task FormalMigrationPreservesLegacyIssuanceAndHasNoPendingModelChanges()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issuance = await ApiFixture.Issue(admin, app, plan);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.GetService<IMigrator>().MigrateAsync("20261008165214_AdministrationRbac");
            await db.Database.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var license = await db.Licenses.SingleAsync(x => x.Id == issuance.Licenses[0].Id); Assert.Null(license.CustomerId); Assert.Null(license.ChannelId);
            var batch = await db.Batches.SingleAsync(x => x.Id == issuance.Batch.Id); Assert.Equal(issuance.Batch.CreatedBy, batch.CreatedBy); Assert.Null(batch.ChannelId);
        }
        using var client = fixture.CreateClient(); Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issuance.Licenses[0].Key, "migration-device"))).StatusCode);
    }
}
