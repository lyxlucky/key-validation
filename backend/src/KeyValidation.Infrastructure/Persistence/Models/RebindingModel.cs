using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<DeviceRebindingEvent> RebindingEvents => Set<DeviceRebindingEvent>();

    partial void ConfigureRebinding(ModelBuilder b)
    {
        b.Entity<License>(e =>
        {
            e.Property(x => x.RebindingEnabled).HasDefaultValue(false);
            e.Property(x => x.RebindingMaxChanges).HasDefaultValue(3);
            e.Property(x => x.RebindingWindowDays).HasDefaultValue(30);
            e.Property(x => x.RebindingCooldownHours).HasDefaultValue(24);
            e.Property(x => x.RebindingGeneration).HasDefaultValue(0L);
            e.ToTable("Licenses", t =>
            {
                t.HasCheckConstraint("CK_Licenses_RebindingMaxChanges", "\"RebindingMaxChanges\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("CK_Licenses_RebindingWindowDays", "\"RebindingWindowDays\" BETWEEN 1 AND 365");
                t.HasCheckConstraint("CK_Licenses_RebindingCooldownHours", "\"RebindingCooldownHours\" BETWEEN 0 AND 8760");
                t.HasCheckConstraint("CK_Licenses_RebindingGeneration", "\"RebindingGeneration\" >= 0");
            });
        });
        b.Entity<DeviceRebindingEvent>(e =>
        {
            e.ToTable("RebindingEvents");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.LicenseId, x.Generation, x.ChangedAt });
            e.HasIndex(x => x.BindingId).IsUnique();
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DeviceBinding>().WithMany().HasForeignKey(x => x.BindingId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
