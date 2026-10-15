using KeyValidation.Domain;
using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options, ApplicationAccessScope? access = null) : IdentityDbContext<AdminUser>(options), ICommerceData
{
    public DbSet<SoftwareApplication> Applications => Set<SoftwareApplication>();
    public DbSet<LicensePlan> Plans => Set<LicensePlan>();
    public DbSet<LicenseBatch> Batches => Set<LicenseBatch>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<DeviceBinding> Bindings => Set<DeviceBinding>();
    public DbSet<LicenseSession> Sessions => Set<LicenseSession>();
    public DbSet<AdminAuditLog> AuditLogs => Set<AdminAuditLog>();
    public DbSet<AuthorizationEvent> AuthorizationEvents => Set<AuthorizationEvent>();
    public DbSet<LicenseOperationReceipt> OperationReceipts => Set<LicenseOperationReceipt>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        ConfigureAccountSecurity(b);
        ConfigureRebinding(b);
        ConfigureAuthorizationPolicies(b);
        ConfigureAccess(b);
        ConfigureRbac(b);
        ConfigureAuditDisplay(b);
        ConfigureReliability(b);
        ConfigureCommerce(b);
        ConfigurePlanCatalog(b);
        ConfigureClientExtensions(b);
        b.Entity<AdminUser>().Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        b.Entity<License>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.Tags).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
            e.HasIndex(x => x.ExpiresAt);
        });
        b.Entity<LicenseOperationReceipt>(e =>
        {
            e.ToTable("OperationReceipts");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.ActorId, x.RequestId }).IsUnique();
        });
        b.Entity<SoftwareApplication>(e =>
        {
            e.ToTable("Applications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<LicensePlan>(e =>
        {
            e.ToTable("Plans", t =>
            {
                t.HasCheckConstraint("CK_Plans_Duration", "\"DurationSeconds\" BETWEEN 1 AND 315360000");
                t.HasCheckConstraint("CK_Plans_Devices", "\"MaxDevices\" BETWEEN 1 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => new { x.CreatedAt, x.Id });
        });
        b.Entity<LicenseBatch>(e =>
        {
            e.ToTable("Batches", t => t.HasCheckConstraint("CK_Batches_Quantity", "\"Quantity\" BETWEEN 1 AND 1000"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.RequestFingerprint).HasMaxLength(64);
            e.Property(x => x.CreatedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.CreatedBy, x.RequestId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicensePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<License>(e =>
        {
            e.ToTable("Licenses", t =>
            {
                t.HasCheckConstraint("CK_Licenses_Duration", "\"DurationSeconds\" BETWEEN 1 AND 315360000");
                t.HasCheckConstraint("CK_Licenses_Devices", "\"MaxDevices\" BETWEEN 1 AND 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.KeyDigest).HasMaxLength(64);
            e.Property(x => x.DigestVersion).HasMaxLength(8);
            e.Property(x => x.KeyHint).HasMaxLength(32);
            e.HasIndex(x => x.KeyDigest).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicenseBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DeviceBinding>(e =>
        {
            e.ToTable("Bindings");
            e.HasKey(x => x.Id);
            e.Property(x => x.DeviceDigest).HasMaxLength(64);
            e.Property(x => x.DeviceLabel).HasMaxLength(120);
            e.HasIndex(x => new { x.LicenseId, x.DeviceDigest }).IsUnique().HasFilter("\"ReleasedAt\" IS NULL");
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LicenseSession>(e =>
        {
            e.ToTable("Sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenDigest).HasMaxLength(64);
            e.HasIndex(x => x.TokenDigest).IsUnique();
            e.HasIndex(x => x.BindingId).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
            e.HasIndex(x => x.LeaseExpiresAt);
            e.HasOne<DeviceBinding>().WithMany().HasForeignKey(x => x.BindingId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AdminAuditLog>(e =>
        {
            e.ToTable("AuditLogs");
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.TraceId).HasMaxLength(128);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => new { x.TargetId, x.CreatedAt });
        });
        b.Entity<AuthorizationEvent>(e =>
        {
            e.ToTable("AuthorizationEvents");
            e.HasKey(x => x.Id);
            e.Property(x => x.Outcome).HasMaxLength(80);
            e.Property(x => x.DeviceHint).HasMaxLength(16);
            e.HasIndex(x => x.CreatedAt);
        });
        TableNamingConvention.Apply(b);
    }

    partial void ConfigureAccountSecurity(ModelBuilder b);
    partial void ConfigureRebinding(ModelBuilder b);
    partial void ConfigureAuthorizationPolicies(ModelBuilder b);
    partial void ConfigureAccess(ModelBuilder b);
    partial void ConfigureReliability(ModelBuilder b);
    partial void ConfigureCommerce(ModelBuilder b);
    partial void ConfigurePlanCatalog(ModelBuilder b);
}
