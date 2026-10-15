using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    private bool AccessAllApplications => access?.AllApplications ?? true;
    private Guid[] AccessApplicationIds => access?.ApplicationIds ?? [];
    private Guid? AccessChannelId => access?.ChannelId;
    private Guid? AccessCustomerId => access?.CustomerId;
    private bool AccessUnrestricted => AccessAllApplications && AccessChannelId == null && AccessCustomerId == null;

    partial void ConfigureAccess(ModelBuilder b)
    {
        b.Entity<AdminUser>(e =>
        {
            e.Property(x => x.AccessConfigured).HasDefaultValue(false);
            e.Property(x => x.AccessAllApplications).HasDefaultValue(true);
            e.Property(x => x.AccessApplicationIds).HasColumnType("uuid[]").HasDefaultValueSql("ARRAY[]::uuid[]");
            e.Property(x => x.AccessPermissions).HasColumnType("text[]").HasDefaultValueSql("ARRAY[]::text[]");
        });
        b.Entity<SoftwareApplication>().HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.Id)) && (AccessChannelId == null || Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.Id))));
        b.Entity<ApplicationPlan>().HasQueryFilter(x => Applications.Any(a => a.Id == x.ApplicationId));
        b.Entity<LicensePlan>().HasQueryFilter(x => AccessUnrestricted || ApplicationPlans.Any(r => r.PlanId == x.Id));
        b.Entity<LicenseBatch>().HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId))) && (AccessCustomerId == null || x.CustomerId == AccessCustomerId));
        b.Entity<License>().HasQueryFilter(x => (AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId))) && (AccessCustomerId == null || x.CustomerId == AccessCustomerId));
        b.Entity<DeviceBinding>().HasQueryFilter(x => AccessUnrestricted || Licenses.Any(l => l.Id == x.LicenseId));
        b.Entity<LicenseSession>().HasQueryFilter(x => AccessUnrestricted || Bindings.Any(binding => binding.Id == x.BindingId));
        b.Entity<DeviceRebindingEvent>().HasQueryFilter(x => AccessUnrestricted || Licenses.Any(l => l.Id == x.LicenseId));
        b.Entity<LicenseFreeze>().HasQueryFilter(x => AccessUnrestricted || Licenses.Any(l => l.Id == x.LicenseId));
        b.Entity<TrialPolicy>().HasQueryFilter(x => AccessAllApplications || AccessApplicationIds.Contains(x.ApplicationId));
        b.Entity<TrialClaim>().HasQueryFilter(x => AccessUnrestricted || Licenses.Any(l => l.Id == x.LicenseId));
        b.Entity<AuthorizationEvent>().HasQueryFilter(x => (AccessAllApplications || x.ApplicationId != null && AccessApplicationIds.Contains(x.ApplicationId.Value)) && (AccessChannelId == null && AccessCustomerId == null || x.LicenseId != null && Licenses.Any(l => l.Id == x.LicenseId)));
        b.Entity<AdminAuditLog>(e =>
        {
            e.HasIndex(x => new { x.ApplicationId, x.CreatedAt });
            e.HasQueryFilter(x => (AccessAllApplications || x.ApplicationId != null && AccessApplicationIds.Contains(x.ApplicationId.Value)) && (AccessChannelId == null || x.ChannelId == AccessChannelId && x.ApplicationId != null && Channels.Any(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(x.ApplicationId.Value))));
        });
        b.Entity<License>().HasIndex(x => new { x.ApplicationId, x.ActivatedAt });
        b.Entity<DeviceBinding>().HasIndex(x => new { x.DeviceDigest, x.LicenseId });
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        if (ChangeTracker.Entries<ChannelQuotaEntry>().Any(x => x.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("Quota ledger entries are immutable.");
        var permissions = WritePermissions();
        await using var transaction = permissions.Length > 0 && Database.CurrentTransaction is null ? await Database.BeginTransactionAsync(cancellationToken) : null;
        if (permissions.Length > 0)
        {
            foreach (var permission in permissions)
                await access!.RecheckWrite(permission, cancellationToken);
            await ValidateWriteApplications(cancellationToken);
        }

        await AssignAuditApplications(cancellationToken);
        await PopulateAuditDisplay(cancellationToken);
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private string[] WritePermissions()
    {
        if (access?.IsAdministrativeRequest != true)
            return[];
        var permissions = access.RequiredWritePermissions.ToHashSet(StringComparer.Ordinal);
        var entries = ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (entries.Any(x => x.Entity is LicensePlan or ApplicationPlan))
        {
            access!.RequireGlobalPlanManagement(write: true);
            return[ApplicationPermissions.Administrator];
        }

        if (permissions.Contains(ApplicationPermissions.Administrator) || entries.Any(x => x.Entity is SoftwareApplication or LicensePlan or TrialPolicy or ApplicationVersionPolicy))
            return[ApplicationPermissions.Administrator];
        if (entries.Any(x => x.Entity is LicenseBatch batch && !access.IsOrderBatch(batch.Id) || x.Entity is License license && x.State == EntityState.Added && !access.IsOrderLicense(license.Id)))
            permissions.Add(ApplicationPermissions.IssueLicenses);
        if (entries.Any(x => x.Entity is License license && x.State != EntityState.Added && !access.IsOrderLicense(license.Id) || x.Entity is DeviceBinding binding && !access.IsOrderBinding(binding.Id) || x.Entity is LicenseSession session && !access.IsOrderBinding(session.BindingId) || x.Entity is DeviceRebindingEvent rebinding && !access.IsOrderBinding(rebinding.BindingId) || x.Entity is LicenseFreeze or LicenseOperationReceipt or OfflineLicenseRecord))
            permissions.Add(ApplicationPermissions.ManageLicenses);
        if (entries.Any(x => x.Entity is Customer))
            permissions.Add(ApplicationPermissions.ManageCustomers);
        if (entries.Any(x => x.Entity is CommerceOrder or OrderLicense or OrderHistory or PortalRequest))
            permissions.Add(ApplicationPermissions.ManageOrders);
        if (entries.Any(x => x.Entity is Channel channel && !access.IsOrderChannel(channel.Id) || x.Entity is ChannelPrice or ApiCredential))
            permissions.Add(ApplicationPermissions.Administrator);
        return permissions.ToArray();
    }

    private async Task ValidateWriteApplications(CancellationToken cancellationToken)
    {
        if (AccessChannelId != null && ChangeTracker.Entries().Any(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted && x.Entity is WebhookSubscription or WebhookDelivery))
            throw new RuleException("FORBIDDEN", 403);
        if (AccessUnrestricted)
            return;
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray())
        {
            var applicationId = entry.Entity switch
            {
                SoftwareApplication x => (Guid? )x.Id,
                ApplicationVersionPolicy x => x.ApplicationId,
                OfflineLicenseRecord x => x.ApplicationId,
                ApplicationPlan x => x.ApplicationId,
                LicenseBatch x => x.ApplicationId,
                License x => x.ApplicationId,
                Customer x => x.ApplicationId,
                CommerceOrder x => x.ApplicationId,
                PortalRequest x => x.ApplicationId,
                ApiCredential x => x.ApplicationId,
                ChannelPrice x => x.ApplicationId,
                RiskPolicy x => x.ApplicationId,
                RiskEvent x => x.ApplicationId,
                NotificationPolicy x => x.ApplicationId,
                OperationsEvent x => x.ApplicationId,
                StaffNotification x => x.ApplicationId,
                WebhookSubscription x => x.ApplicationId,
                WebhookDelivery x => x.ApplicationId,
                _ => null
            };
            if (applicationId is { } id && (!access!.Includes(id) || AccessChannelId != null && !await Channels.AnyAsync(c => c.Id == AccessChannelId && c.ApplicationIds.Contains(id), cancellationToken)))
                throw new RuleException("NOT_FOUND", 404);
            var(hasChannel, channelId) = entry.Entity switch
            {
                License x => (true, x.ChannelId),
                OfflineLicenseRecord x => (true, x.ChannelId),
                LicenseBatch x => (true, x.ChannelId),
                Customer x => (true, x.ChannelId),
                CommerceOrder x => (true, x.ChannelId),
                PortalRequest x => (true, x.ChannelId),
                OperationsEvent x => (true, x.ChannelId),
                StaffNotification x => (true, x.ChannelId),
                ApiCredential x => (true, x.ChannelId),
                ChannelQuotaEntry x => (true, (Guid? )x.ChannelId),
                ChannelPrice x => (true, (Guid? )x.ChannelId),
                _ => (false, (Guid? )null)};
            if (hasChannel && !access!.IncludesChannel(channelId))
                throw new RuleException("NOT_FOUND", 404);
            var licenseId = entry.Entity switch
            {
                DeviceBinding x => (Guid? )x.LicenseId,
                DeviceRebindingEvent x => x.LicenseId,
                LicenseFreeze x => x.LicenseId,
                OfflineLicenseRecord x => x.LicenseId,
                _ => null
            };
            if (licenseId is { } parent && !Licenses.Local.Any(x => x.Id == parent && access!.Includes(x.ApplicationId) && access.IncludesChannel(x.ChannelId)) && !await Licenses.AnyAsync(x => x.Id == parent, cancellationToken))
                throw new RuleException("NOT_FOUND", 404);
            if (entry.Entity is LicenseSession session && !await Bindings.AnyAsync(x => x.Id == session.BindingId, cancellationToken))
                throw new RuleException("NOT_FOUND", 404);
        }
    }

    private async Task AssignAuditApplications(CancellationToken cancellationToken)
    {
        foreach (var audit in ChangeTracker.Entries<AdminAuditLog>().Where(x => x.State == EntityState.Added && x.Entity.ApplicationId == null).Select(x => x.Entity).ToArray())
        {
            var target = audit.TargetId;
            if (audit.Action.StartsWith("application.", StringComparison.Ordinal))
                audit.ApplicationId = target;
            else if (audit.Action.StartsWith("batch.", StringComparison.Ordinal))
                audit.ApplicationId = Batches.Local.FirstOrDefault(x => x.Id == target)?.ApplicationId ?? await Batches.Where(x => x.Id == target).Select(x => (Guid? )x.ApplicationId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("license.", StringComparison.Ordinal))
                audit.ApplicationId = Licenses.Local.FirstOrDefault(x => x.Id == target)?.ApplicationId ?? await Licenses.Where(x => x.Id == target).Select(x => (Guid? )x.ApplicationId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("binding.", StringComparison.Ordinal))
                audit.ApplicationId = await (
                    from binding in Bindings
                    join license in Licenses on binding.LicenseId equals license.Id
                    where binding.Id == target
                    select (Guid? )license.ApplicationId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("session.", StringComparison.Ordinal))
                audit.ApplicationId = await (
                    from session in Sessions
                    join binding in Bindings on session.BindingId equals binding.Id
                    join license in Licenses on binding.LicenseId equals license.Id
                    where session.Id == target
                    select (Guid? )license.ApplicationId).SingleOrDefaultAsync(cancellationToken);
        }

        foreach (var audit in ChangeTracker.Entries<AdminAuditLog>().Where(x => x.State == EntityState.Added && x.Entity.ChannelId == null).Select(x => x.Entity).ToArray())
        {
            var target = audit.TargetId;
            if (audit.Action.StartsWith("batch.", StringComparison.Ordinal))
                audit.ChannelId = Batches.Local.FirstOrDefault(x => x.Id == target)?.ChannelId ?? await Batches.Where(x => x.Id == target).Select(x => x.ChannelId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("license.", StringComparison.Ordinal))
                audit.ChannelId = Licenses.Local.FirstOrDefault(x => x.Id == target)?.ChannelId ?? await Licenses.Where(x => x.Id == target).Select(x => x.ChannelId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("binding.", StringComparison.Ordinal))
                audit.ChannelId = await (
                    from binding in Bindings
                    join license in Licenses on binding.LicenseId equals license.Id
                    where binding.Id == target
                    select license.ChannelId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("session.", StringComparison.Ordinal))
                audit.ChannelId = await (
                    from session in Sessions
                    join binding in Bindings on session.BindingId equals binding.Id
                    join license in Licenses on binding.LicenseId equals license.Id
                    where session.Id == target
                    select license.ChannelId).SingleOrDefaultAsync(cancellationToken);
            else if (audit.Action.StartsWith("risk.", StringComparison.Ordinal))
                audit.ChannelId = await (
                    from risk in RiskEvents
                    join license in Licenses on risk.LicenseId equals license.Id
                    where risk.Id == target
                    select license.ChannelId).SingleOrDefaultAsync(cancellationToken);
        }
    }
}
