using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class SharedPlanIssuanceTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task OnePlanIssuesIntoTwoApplicationsButNotAnUnreferencedApplication()
    {
        var (admin, first, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (secondAdmin, second, _) = await fixture.Catalog();
        using var secondCleanup = secondAdmin;
        var (thirdAdmin, third, _) = await fixture.Catalog();
        using var thirdCleanup = thirdAdmin;
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id);

        var a = await ApiFixture.Issue(admin, first, plan);
        var b = await ApiFixture.Issue(admin, second, plan);
        Assert.Equal(first.Id, a.Batch.ApplicationId);
        Assert.Equal(second.Id, b.Batch.ApplicationId);
        Assert.Equal(plan.Id, b.Batch.PlanId);
        var rejected = await admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(third.Id, plan.Id, 1, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);

        var catalog = (await admin.GetFromJsonAsync<ReportsCatalogDto>("/api/admin/catalog"))!;
        var options = catalog.Plans.Where(x => x.Id == plan.Id).ToArray();
        Assert.Equal(2, options.Length);
        Assert.Contains(options, x => x.ApplicationId == first.Id && x.IsEnabled);
        Assert.Contains(options, x => x.ApplicationId == second.Id && x.IsEnabled);
        var report = (await admin.GetFromJsonAsync<OperationsReport>(
            $"/api/admin/reports/operations?planId={plan.Id}"))!;
        Assert.Equal(2, report.Totals.Issued);
        Assert.Equal(2, report.Breakdown.Count);
        Assert.Contains(report.Breakdown, x => x.ApplicationId == first.Id && x.Issued == 1);
        Assert.Contains(report.Breakdown, x => x.ApplicationId == second.Id && x.Issued == 1);
    }

    [Fact]
    public async Task PausingOneReferenceKeepsItsOldCardAndOtherApplicationUsable()
    {
        var (admin, first, plan) = await fixture.Catalog();
        using var cleanup = admin;
        var (otherAdmin, second, _) = await fixture.Catalog();
        using var otherCleanup = otherAdmin;
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id);
        var oldCard = await ApiFixture.Issue(admin, second, plan);
        await SharedPlanTestData.Reference(fixture, second.Id, plan.Id, false);

        var rejected = await admin.PostAsJsonAsync("/api/admin/license-batches",
            new IssueBatch(second.Id, plan.Id, 1, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        await ApiFixture.Issue(admin, first, plan);
        var history = (await admin.GetFromJsonAsync<PagedResult<PlanDto>>(
            $"/api/admin/applications/{second.Id}/plans"))!;
        Assert.False(Assert.Single(history.Items, x => x.Id == plan.Id).IsEnabled);
        using var client = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            "/api/client/licenses/activate",
            new ActivateLicense(second.Code, oldCard.Licenses[0].Key, "shared-old-card"))).StatusCode);
    }
}
