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

public sealed class OperationsAccessMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task PreviousReleaseAuditsGainApplicationOwnershipAndAccountsKeepLegacyAccess()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); var licenseId = batch.Licenses[0].Id;
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "migration device"))).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!;
        var binding = Assert.Single(detail.Bindings); var session = Assert.Single(binding.Sessions);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/sessions/{session.Id}/revoke", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/device-bindings/{binding.Id}/release", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/licenses/{licenseId}/disable", null)).StatusCode);

        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
            // Reconstruct the old release's audit input: reference actions did not exist then.
            await db.AuditLogs.Where(x => x.TargetId == plan.Id && x.Action == "application-plan.bind").ExecuteDeleteAsync();
            await migrator.MigrateAsync("20261006074346_AuthorizationPolicies");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var targets = new[] { app.Id, plan.Id, batch.Batch.Id, licenseId, binding.Id, session.Id };
            var audits = await db.AuditLogs.AsNoTracking().Where(x => targets.Contains(x.TargetId)).ToArrayAsync();
            Assert.Equal(6, audits.Length);
            Assert.All(audits, audit => Assert.Equal(app.Id, audit.ApplicationId));
            Assert.All(await db.Users.AsNoTracking().ToArrayAsync(), user => Assert.False(user.AccessConfigured));
        }
        using var reader = await fixture.Login("readonly");
        var access = (await reader.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!;
        Assert.True(access.AllApplications); Assert.Contains("audit.read", access.Permissions);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync($"/api/admin/licenses/{licenseId}")).StatusCode);
    }
}
