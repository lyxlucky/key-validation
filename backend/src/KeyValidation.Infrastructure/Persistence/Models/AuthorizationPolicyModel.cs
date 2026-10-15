using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<LicenseFreeze> LicenseFreezes => Set<LicenseFreeze>();
    public DbSet<TrialPolicy> TrialPolicies => Set<TrialPolicy>();
    public DbSet<TrialClaim> TrialClaims => Set<TrialClaim>();

    partial void ConfigureAuthorizationPolicies(ModelBuilder b)
    {
        b.Entity<LicensePlan>(e =>
        {
            e.Property(x => x.Features).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
            e.ToTable("Plans", t =>
            {
                t.HasCheckConstraint("CK_Plans_Sessions", "\"MaxConcurrentSessions\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("CK_Plans_ActivationDays", "\"ActivationValidityDays\" IS NULL OR \"ActivationValidityDays\" BETWEEN 1 AND 3650");
            });
        });
        b.Entity<License>(e =>
        {
            e.Property(x => x.Features).HasField("_features").UsePropertyAccessMode(PropertyAccessMode.Field).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
            e.Ignore(x => x.FrozenRemainingSeconds);
            e.ToTable("Licenses", t => t.HasCheckConstraint("CK_Licenses_Sessions", "\"MaxConcurrentSessions\" BETWEEN 1 AND 100"));
            e.HasIndex(x => x.ActivateBefore);
        });
        b.Entity<LicenseFreeze>(e =>
        {
            e.ToTable("LicenseFreezes");
            e.HasKey(x => x.Id);
            e.Property(x => x.FrozenBy).HasMaxLength(450);
            e.Property(x => x.UnfrozenBy).HasMaxLength(450);
            e.Property(x => x.FreezeReason).HasMaxLength(500);
            e.Property(x => x.UnfreezeReason).HasMaxLength(500);
            e.HasIndex(x => new { x.LicenseId, x.FrozenAt });
            e.HasIndex(x => x.LicenseId).IsUnique().HasFilter("\"UnfrozenAt\" IS NULL");
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrialPolicy>(e =>
        {
            e.ToTable("TrialPolicies", t => t.HasCheckConstraint("CK_TrialPolicies_Duration", "\"DurationSeconds\" BETWEEN 1 AND 2592000"));
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.Features).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
            e.HasOne<SoftwareApplication>().WithOne().HasForeignKey<TrialPolicy>(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TrialClaim>(e =>
        {
            e.ToTable("TrialClaims");
            e.HasKey(x => x.Id);
            e.Property(x => x.DeviceDigest).HasMaxLength(64);
            e.HasIndex(x => new { x.ApplicationId, x.DeviceDigest }).IsUnique();
            e.HasIndex(x => x.LicenseId).IsUnique();
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.UpgradedToLicenseId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
