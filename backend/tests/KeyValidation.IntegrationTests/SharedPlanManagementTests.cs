using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanCatalogTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task GlobalPlanCanExistUnboundAndBeReferencedWithoutCopying()
    {
        var (admin, a, _) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        var response = await admin.PostAsJsonAsync("/api/admin/plans",
            new CreatePlan("共享月卡", 2592000, 2, 1, 30, ["export"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var plan = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        Assert.Equal(0, plan.ReferenceCount);
        foreach (var app in new[] { a, b })
        {
            for (var attempt = 0; attempt < 2; attempt++)
                Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
                    $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
                    new SetPlanReference(true))).StatusCode);
            var page = (await admin.GetFromJsonAsync<PagedResult<PlanDto>>(
                $"/api/admin/applications/{app.Id}/plans"))!;
            var option = Assert.Single(page.Items, x => x.Id == plan.Id);
            Assert.Equal(app.Id, option.ApplicationId);
            await ApiFixture.Issue(admin, app, option);
        }
        var saved = (await admin.GetFromJsonAsync<GlobalPlanDto>(
            $"/api/admin/plans/{plan.Id}"))!;
        Assert.Equal(2, saved.ReferenceCount);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Plans.CountAsync(x => x.Id == plan.Id));
        Assert.Equal(2, await db.ApplicationPlans.CountAsync(x => x.PlanId == plan.Id));
        Assert.Equal(2, await db.AuditLogs.CountAsync(
            x => x.TargetId == plan.Id && x.Action == "application-plan.bind"));
    }

    [Fact]
    public async Task ReferenceRequiresExplicitStateAndGlobalUpdatesReturnGlobalDto()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}", new { })).StatusCode);
        var response = await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}",
            new UpdatePlan("全局日卡", 86400, 3, true, 2, null, ["basic"]));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var saved = (await response.Content.ReadFromJsonAsync<GlobalPlanDto>())!;
        Assert.Equal(3, saved.MaxDevices);
        Assert.Equal(2, saved.MaxConcurrentSessions);
        Assert.Equal(1, saved.ReferenceCount);
    }
}
