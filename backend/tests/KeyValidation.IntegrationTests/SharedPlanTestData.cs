using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace KeyValidation.IntegrationTests;

internal static class SharedPlanTestData
{
    internal static async Task Reference(ApiFixture fixture, Guid applicationId,
        Guid planId, bool enabled = true)
    {
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.ApplicationPlans.SingleOrDefaultAsync(
            x => x.ApplicationId == applicationId && x.PlanId == planId);
        if (row is null)
        {
            row = new ApplicationPlan
            {
                ApplicationId = applicationId, PlanId = planId,
                CreatedAt = fixture.Clock.GetUtcNow()
            };
            db.ApplicationPlans.Add(row);
        }
        row.IsEnabled = enabled;
        await db.SaveChangesAsync();
    }
}
