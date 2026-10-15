using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class OperationsScanner(AppDbContext db, ApplicationAccessScope access, RiskService risk, NotificationService notifications, TimeProvider clock)
{
    public async Task<OperationsScanDto> ScanAsync(CancellationToken cancellationToken = default)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 1)", cancellationToken);
        // Licensing writers take FOR SHARE before timestamping their facts and
        // hold it through commit. Lock the same parent rows in PostgreSQL UUID
        // order, then capture the cutoff. A writer cannot straddle that cutoff.
        var applications = await db.Database.SqlQueryRaw<Guid>("SELECT \"Id\" AS \"Value\" FROM \"t_applications\" ORDER BY \"Id\" FOR UPDATE").ToArrayAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var riskCount = await risk.Scan(applications, now, cancellationToken);
        await notifications.PublishExpirations(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        var dispatched = await notifications.Dispatch(now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(now, riskCount, dispatched.Events, dispatched.Notifications, dispatched.Deliveries);
    }
}
