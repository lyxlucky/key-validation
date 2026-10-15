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

public sealed class ClientExtensionsMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task AdditiveMigrationPreservesExistingActivatedLicenseBindingAndSession()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var owner = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "legacy-migration-device"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var grant = (await response.Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", grant.SessionToken);
        var before = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{grant.LicenseId}"))!;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await db.GetService<IMigrator>().MigrateAsync("20261009021411_CommerceIntegrations");
            await db.Database.MigrateAsync(); Assert.False(db.Database.HasPendingModelChanges());
            var session = await db.Sessions.SingleAsync(x => x.Id == Assert.Single(Assert.Single(before.Bindings).Sessions).Id);
            Assert.Null(session.ClientVersion); Assert.Null(session.RevokedAt); Assert.Equal(grant.LeaseExpiresAt, session.LeaseExpiresAt);
            Assert.False(await db.OfflineLicenses.AnyAsync()); Assert.False(await db.VersionPolicies.AnyAsync());
        }
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        var after = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{grant.LicenseId}"))!;
        Assert.Equal(before.License.ExpiresAt, after.License.ExpiresAt); Assert.Equal(Assert.Single(before.Bindings).Id, Assert.Single(after.Bindings).Id);
    }
}
