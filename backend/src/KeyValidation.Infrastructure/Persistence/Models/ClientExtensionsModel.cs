using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<ApplicationVersionPolicy> VersionPolicies => Set<ApplicationVersionPolicy>();
    public DbSet<OfflineLicenseRecord> OfflineLicenses => Set<OfflineLicenseRecord>();

    private void ConfigureClientExtensions(ModelBuilder b)
    {
        b.Entity<LicenseSession>().Property(x => x.ClientVersion).HasMaxLength(14);
        b.Entity<ApplicationVersionPolicy>(e =>
        {
            e.ToTable("ApplicationVersionPolicies");
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.MinimumVersion).HasMaxLength(14);
            e.Property(x => x.LatestVersion).HasMaxLength(14);
            e.Property(x => x.UpdateUrl).HasMaxLength(2048);
            e.Property(x => x.ReleaseNotes).HasMaxLength(4000);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.HasOne<SoftwareApplication>().WithOne().HasForeignKey<ApplicationVersionPolicy>(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Applications.Any(a => a.Id == x.ApplicationId));
        });
        b.Entity<OfflineLicenseRecord>(e =>
        {
            e.ToTable("OfflineLicenses", t => t.HasCheckConstraint("CK_OfflineLicenses_Times", "\"IssuedAt\" <= \"NotBefore\" AND \"NotBefore\" < \"ExpiresAt\" AND \"ExpiresAt\" <= \"IssuedAt\" + INTERVAL '168 hours'"));
            e.HasKey(x => x.Id);
            e.Property(x => x.KeyId).HasMaxLength(64);
            e.Property(x => x.IssuedBy).HasMaxLength(450);
            e.Property(x => x.ContentSha256).HasMaxLength(64);
            e.Property(x => x.RevocationReason).HasMaxLength(32);
            e.Property(x => x.Features).HasColumnType("text[]");
            e.HasIndex(x => x.Serial).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.IssuedAt });
            e.HasIndex(x => new { x.LicenseId, x.IssuedAt });
            e.HasIndex(x => x.BindingId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DeviceBinding>().WithMany().HasForeignKey(x => x.BindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Licenses.Any(license => license.Id == x.LicenseId && license.ApplicationId == x.ApplicationId));
        });
    }
}
