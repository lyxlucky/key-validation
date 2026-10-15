using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CommerceOrder> Orders => Set<CommerceOrder>();
    public DbSet<OrderLicense> OrderLicenses => Set<OrderLicense>();
    public DbSet<OrderHistory> OrderHistories => Set<OrderHistory>();
    public DbSet<CommerceReceipt> CommerceReceipts => Set<CommerceReceipt>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelPrice> ChannelPrices => Set<ChannelPrice>();
    public DbSet<ChannelQuotaEntry> ChannelQuotaEntries => Set<ChannelQuotaEntry>();
    public DbSet<PortalSession> PortalSessions => Set<PortalSession>();
    public DbSet<PortalRequest> PortalRequests => Set<PortalRequest>();
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();

    partial void ConfigureCommerce(ModelBuilder b)
    {
        b.Entity<Channel>(e =>
        {
            e.ToTable("Channels", t => t.HasCheckConstraint("CK_Channels_Quota", "\"RemainingQuota\" >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.AccountId).HasMaxLength(450);
            e.HasIndex(x => x.AccountId).IsUnique();
            e.HasOne<AdminUser>().WithOne().HasForeignKey<Channel>(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessChannelId == null || x.Id == AccessChannelId);
        });
        b.Entity<Customer>(e =>
        {
            e.ToTable("Customers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.AccessCodeDigest).HasMaxLength(64);
            e.HasIndex(x => x.AccessCodeDigest).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.ChannelId, x.CreatedAt });
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId))) && (AccessCustomerId == null || x.Id == AccessCustomerId));
        });
        b.Entity<CommerceOrder>(e =>
        {
            e.ToTable("Orders", t =>
            {
                t.HasCheckConstraint("CK_Orders_Quantity", "\"Quantity\" BETWEEN 1 AND 100");
                t.HasCheckConstraint("CK_Orders_Price", "\"UnitPriceCents\" >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.PlanName).HasMaxLength(120);
            e.Property(x => x.CreatedBy).HasMaxLength(450);
            e.Property(x => x.FulfilledBy).HasMaxLength(450);
            e.Property(x => x.RequestFingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.CreatedBy, x.RequestId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.ChannelId, x.CreatedAt });
            e.HasIndex(x => x.DeliveryExpiresAt).HasFilter("\"DeliveryCiphertext\" IS NOT NULL");
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicensePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.RenewalLicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicenseBatch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Customers.Any(c => c.Id == x.CustomerId));
        });
        b.Entity<OrderLicense>(e =>
        {
            e.HasKey(x => new { x.OrderId, x.LicenseId });
            e.HasOne<CommerceOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Orders.Any(o => o.Id == x.OrderId));
        });
        b.Entity<OrderHistory>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(32);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.HasOne<CommerceOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.OrderId, x.CreatedAt });
            e.HasQueryFilter(x => Orders.Any(o => o.Id == x.OrderId));
        });
        b.Entity<CommerceReceipt>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.Operation).HasMaxLength(32);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.ActorId, x.Operation, x.RequestId }).IsUnique();
        });
        b.Entity<ChannelPrice>(e =>
        {
            e.ToTable("ChannelPrices", t => t.HasCheckConstraint("CK_ChannelPrices_Price", "\"UnitPriceCents\" >= 0"));
            e.HasKey(x => new { x.ChannelId, x.ApplicationId, x.PlanId });
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LicensePlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId));
        });
        b.Entity<ChannelQuotaEntry>(e =>
        {
            e.ToTable("ChannelQuotaEntries", t => t.HasCheckConstraint("CK_ChannelQuotaEntries_Balance", "\"BalanceAfter\" >= 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.ActorId).HasMaxLength(450);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.ChannelId, x.Kind, x.RequestId }).IsUnique();
            e.HasIndex(x => x.OrderId).IsUnique();
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CommerceOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessChannelId == null || x.ChannelId == AccessChannelId);
        });
        b.Entity<PortalSession>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenDigest).HasMaxLength(64);
            e.HasIndex(x => x.TokenDigest).IsUnique();
            e.HasIndex(x => new { x.CustomerId, x.ExpiresAt });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Customers.Any(c => c.Id == x.CustomerId));
        });
        b.Entity<PortalRequest>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(500);
            e.Property(x => x.ReviewReason).HasMaxLength(500);
            e.Property(x => x.ReviewedBy).HasMaxLength(450);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.ReviewFingerprint).HasMaxLength(64);
            e.HasIndex(x => new { x.CustomerId, x.RequestId }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.ChannelId, x.CreatedAt });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<License>().WithMany().HasForeignKey(x => x.LicenseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<DeviceBinding>().WithMany().HasForeignKey(x => x.BindingId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CommerceOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => Customers.Any(c => c.Id == x.CustomerId));
        });
        b.Entity<ApiCredential>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.SecretDigest).HasMaxLength(64);
            e.HasIndex(x => x.SecretDigest).IsUnique();
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId));
        });
        b.Entity<License>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<License>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<License>().HasIndex(x => new { x.ApplicationId, x.ChannelId, x.CreatedAt });
        b.Entity<LicenseBatch>().HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LicenseBatch>().HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<LicenseBatch>().HasIndex(x => new { x.ApplicationId, x.ChannelId, x.CreatedAt });
    }
}
