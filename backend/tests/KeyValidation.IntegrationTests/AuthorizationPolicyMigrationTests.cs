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

public sealed class AuthorizationPolicyMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact] public async Task PreviousReleaseCardsAndPlansBackfillOnlineLimitsAndRemainActivatable()
    {
        var (admin, app, _) = await fixture.Catalog(); using var adminDispose = admin;
        var response = await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("旧卡种", 86400, 3));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); var plan = (await response.Content.ReadFromJsonAsync<PlanDto>())!;
        var issued = await ApiFixture.Issue(admin, app, plan);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261005154844_AccountSecurityRebinding");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
        }
        var card = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/licenses/{issued.Licenses[0].Id}");
        var license = card.GetProperty("license");
        Assert.Equal(3, license.GetProperty("maxConcurrentSessions").GetInt32());
        Assert.Equal(JsonValueKind.Null, license.GetProperty("activateBefore").ValueKind); Assert.Empty(license.GetProperty("features").EnumerateArray()); Assert.False(license.GetProperty("isTrial").GetBoolean());
        var plans = (await admin.GetFromJsonAsync<PagedResult<PlanDto>>($"/api/admin/applications/{app.Id}/plans"))!;
        Assert.Equal(3, Assert.Single(plans.Items, p => p.Id == plan.Id).MaxConcurrentSessions);
        var trial = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/applications/{app.Id}/trial-policy"); Assert.False(trial.GetProperty("isEnabled").GetBoolean());
        using var client = fixture.CreateClient();
        for (var i = 0; i < 3; i++) Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "legacy-" + i))).StatusCode);
    }
}
