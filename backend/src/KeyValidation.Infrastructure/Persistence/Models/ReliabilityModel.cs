using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<RiskPolicy> RiskPolicies => Set<RiskPolicy>();
    public DbSet<RiskEvent> RiskEvents => Set<RiskEvent>();
    public DbSet<NotificationPolicy> NotificationPolicies => Set<NotificationPolicy>();
    public DbSet<OperationsEvent> OperationsEvents => Set<OperationsEvent>();
    public DbSet<StaffNotification> StaffNotifications => Set<StaffNotification>();
    public DbSet<WebhookSubscription> WebhookSubscriptions => Set<WebhookSubscription>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<BackupJob> BackupJobs => Set<BackupJob>();
    public DbSet<MonitoringSample> MonitoringSamples => Set<MonitoringSample>();
    public DbSet<MonitoringIncident> MonitoringIncidents => Set<MonitoringIncident>();

    partial void ConfigureReliability(ModelBuilder b)
    {
        b.Entity<AuthorizationEvent>().Property(x => x.TrustedCountryCode).HasMaxLength(2);
        b.Entity<AuthorizationEvent>().HasIndex(x => new { x.ApplicationId, x.LicenseId, x.CreatedAt });
        b.Entity<RiskPolicy>(e =>
        {
            e.HasKey(x => x.ApplicationId);
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId));
        });
        b.Entity<RiskEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Rule).HasMaxLength(40);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.Property(x => x.ReviewReason).HasMaxLength(1000);
            e.Property(x => x.ReviewedBy).HasMaxLength(450);
            e.HasIndex(x => new { x.LicenseId, x.Rule, x.WindowStart }).IsUnique();
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null && AccessCustomerId == null || Licenses.Any(l => l.Id == x.LicenseId)));
        });
        b.Entity<NotificationPolicy>(e =>
        {
            e.HasKey(x => x.ApplicationId);
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId));
        });
        b.Entity<OperationsEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(80);
            e.Property(x => x.DeduplicationKey).HasMaxLength(64);
            e.HasIndex(x => new { x.ApplicationId, x.DeduplicationKey }).IsUnique();
            e.HasIndex(x => new { x.DispatchedAt, x.CreatedAt });
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId))));
        });
        b.Entity<StaffNotification>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RecipientId).HasMaxLength(450);
            e.Property(x => x.EventType).HasMaxLength(80);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.HasIndex(x => new { x.EventId, x.RecipientId }).IsUnique();
            e.HasIndex(x => new { x.RecipientId, x.CreatedAt });
            e.HasOne<OperationsEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId))));
        });
        b.Entity<WebhookSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.HasOne<SoftwareApplication>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessChannelId == null && (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)));
        });
        b.Entity<WebhookDelivery>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(80);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.LastError).HasMaxLength(80);
            e.HasIndex(x => new { x.EventId, x.SubscriptionId }).IsUnique();
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasOne<OperationsEvent>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<WebhookSubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
            e.HasQueryFilter(x => AccessChannelId == null && (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)));
        });
        b.Entity<BackupJob>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.VerificationStatus).HasMaxLength(16);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.ErrorCode).HasMaxLength(80);
            e.Property(x => x.VerificationErrorCode).HasMaxLength(80);
            e.HasIndex(x => x.CreatedAt);
        });
        b.Entity<MonitoringSample>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.InstanceId).HasMaxLength(32);
            e.HasIndex(x => x.SampledAt);
        });
        b.Entity<MonitoringIncident>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Summary).HasMaxLength(500);
            e.HasIndex(x => x.Kind).IsUnique().HasFilter("\"ResolvedAt\" IS NULL");
            e.HasIndex(x => x.StartedAt);
        });
    }
}
