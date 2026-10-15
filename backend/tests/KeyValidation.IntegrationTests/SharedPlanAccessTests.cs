using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanAccessTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task ScopedPlatformManagerCanUseVisibleReferenceButCannotEditGlobalPlan()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        string id;
        var name = "plan-scope-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = new AdminUser { UserName = name };
            Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Operator")).Succeeded);
            id = user.Id;
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(
            $"/api/admin/accounts/{id}/access",
            new UpdateAccountAccess(
                ["platform.manage", "licenses.read", "licenses.issue"], false, [b.Id]))).StatusCode);
        using var actor = await fixture.Login(name);
        var options = (await actor.GetFromJsonAsync<PagedResult<PlanDto>>(
            $"/api/admin/applications/{b.Id}/plans"))!;
        var shared = Assert.Single(options.Items, x => x.Id == plan.Id);
        await ApiFixture.Issue(actor, b, shared);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync(
            $"/api/admin/applications/{a.Id}/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/plans")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PatchAsJsonAsync(
            $"/api/admin/plans/{plan.Id}", new UpdatePlan("越权", 3600, 1, true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.PutAsJsonAsync(
            $"/api/admin/applications/{b.Id}/plan-references/{plan.Id}",
            new SetPlanReference(false))).StatusCode);

        using var writeScope = fixture.Services.CreateScope();
        var access = writeScope.ServiceProvider.GetRequiredService<ApplicationAccessScope>();
        access.Initialize(new AccountAccessDto(["platform.manage"], false, [b.Id], null),
            true, (_, _) => Task.CompletedTask);
        var db = writeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.Plans.SingleAsync(x => x.Id == plan.Id)).Name = "直接绕过";
        await Assert.ThrowsAsync<RuleException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task OrderOnlyApiCredentialReceivesOnlyItsApplicationPlanPair()
    {
        var (admin, a, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, b, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, b.Id, plan.Id);
        var response = await admin.PostAsJsonAsync("/api/admin/api-credentials",
            new CreateApiCredential(a.Id, "shared-order-api", ["orders.manage"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var credential = (await response.Content.ReadFromJsonAsync<ApiCredentialSecretDto>())!;
        using var api = fixture.CreateClient();
        api.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", credential.Secret);
        var catalog = (await api.GetFromJsonAsync<ReportsCatalogDto>("/api/v1/catalog"))!;
        Assert.DoesNotContain(catalog.Applications, x => x.Id == b.Id);
        var option = Assert.Single(catalog.Plans, x => x.Id == plan.Id);
        Assert.Equal(a.Id, option.ApplicationId);
        Assert.True(option.IsEnabled);
    }


    [Fact]
    public async Task NewWritesKeepCsrfProtection()
    {
        var (admin, app, plan) = await fixture.Catalog();
        using var cleanup = admin;
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            "/api/admin/plans", new CreatePlan("无 CSRF", 3600))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(
            $"/api/admin/applications/{app.Id}/plan-references/{plan.Id}",
            new SetPlanReference(false))).StatusCode);
    }
}
