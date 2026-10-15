using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KeyValidation.Application;

// The application owns transaction choreography. Infrastructure supplies the
// PostgreSQL implementation; Identity and provider configuration stay outside it.
public interface ILicensingData
{
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    DbSet<SoftwareApplication> Applications { get; }
    DbSet<LicensePlan> Plans { get; }
    DbSet<ApplicationPlan> ApplicationPlans { get; }
    DbSet<LicenseBatch> Batches { get; }
    DbSet<License> Licenses { get; }
    DbSet<DeviceBinding> Bindings { get; }
    DbSet<LicenseSession> Sessions { get; }
    DbSet<AdminAuditLog> AuditLogs { get; }
    DbSet<AuthorizationEvent> AuthorizationEvents { get; }
    DbSet<LicenseOperationReceipt> OperationReceipts { get; }
    DbSet<DeviceRebindingEvent> RebindingEvents { get; }
    DbSet<LicenseFreeze> LicenseFreezes { get; }
    DbSet<TrialPolicy> TrialPolicies { get; }
    DbSet<TrialClaim> TrialClaims { get; }
    DbSet<ApplicationVersionPolicy> VersionPolicies { get; }
    DbSet<OfflineLicenseRecord> OfflineLicenses { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
