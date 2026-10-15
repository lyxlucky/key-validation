using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class RebindingMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ApplyingMigrationLeavesExistingCardsUnrestrictedWhileNewCardsUseDefaultLimits()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var oldBatch = await ApiFixture.Issue(admin, app, plan);
        // This fixture owns an isolated PostgreSQL database. Exercise the real old schema,
        // retaining its issued cards across a downgrade and forward migration.
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261005092000_ManagementExpansion");
            await migrator.MigrateAsync();
        }
        var policy = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{oldBatch.Licenses[0].Id}/device-policy");
        Assert.False(policy.GetProperty("enabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, policy.GetProperty("remainingChanges").ValueKind);
        using var client = fixture.CreateClient();
        for (var i = 0; i < 4; i++)
        {
            var activation = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, oldBatch.Licenses[0].Key, "legacy-" + i));
            Assert.Equal(HttpStatusCode.OK, activation.StatusCode);
            client.DefaultRequestHeaders.Authorization = new("Bearer", (await activation.Content.ReadFromJsonAsync<ActivationDto>())!.SessionToken);
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/device-bindings/release", null)).StatusCode);
        }
        policy = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{oldBatch.Licenses[0].Id}/device-policy");
        Assert.Equal(4, policy.GetProperty("usedChanges").GetInt32());
        var newBatch = await ApiFixture.Issue(admin, app, plan);
        policy = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{newBatch.Licenses[0].Id}/device-policy");
        Assert.True(policy.GetProperty("enabled").GetBoolean());
        Assert.Equal(3, policy.GetProperty("remainingChanges").GetInt32());
    }
}
