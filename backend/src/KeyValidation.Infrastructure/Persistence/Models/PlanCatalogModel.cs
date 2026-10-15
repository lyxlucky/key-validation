using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<ApplicationPlan> ApplicationPlans => Set<ApplicationPlan>();

    partial void ConfigurePlanCatalog(ModelBuilder b)
    {
        b.Entity<ApplicationPlan>(e =>
        {
            e.ToTable("ApplicationPlans");
            e.HasKey(x => new { x.ApplicationId, x.PlanId });
            e.HasIndex(x => x.PlanId);
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicensePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LicenseBatch>().HasOne<ApplicationPlan>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.PlanId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<CommerceOrder>().HasOne<ApplicationPlan>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.PlanId }).OnDelete(DeleteBehavior.Restrict);
        b.Entity<ChannelPrice>().HasOne<ApplicationPlan>().WithMany().HasForeignKey(x => new { x.ApplicationId, x.PlanId }).OnDelete(DeleteBehavior.Restrict);
    }
}
