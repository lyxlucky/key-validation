using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class ReliabilityRetention(AppDbContext db, TimeProvider clock)
{
    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 5)", cancellationToken);
        var samples = clock.GetUtcNow().AddDays(-30); var history = clock.GetUtcNow().AddDays(-180);
        var now = clock.GetUtcNow();
        await db.Orders.Where(x => x.DeliveryCiphertext != null && x.DeliveryExpiresAt <= now).ExecuteUpdateAsync(s => s.SetProperty(x => x.DeliveryCiphertext, (string?)null), cancellationToken);
        await db.PortalSessions.Where(x => x.ExpiresAt <= now || x.RevokedAt != null).ExecuteDeleteAsync(cancellationToken);
        await db.MonitoringSamples.Where(x => x.SampledAt < samples).ExecuteDeleteAsync(cancellationToken);
        await db.MonitoringIncidents.Where(x => x.StartedAt < history && x.ResolvedAt < history).ExecuteDeleteAsync(cancellationToken);
        await db.StaffNotifications.Where(x => x.CreatedAt < history).ExecuteDeleteAsync(cancellationToken);
        await db.WebhookDeliveries.Where(x => x.CreatedAt < history && x.CompletedAt < history && (x.Status == "succeeded" || x.Status == "failed" || x.Status == "expired")).ExecuteDeleteAsync(cancellationToken);
        await db.OperationsEvents.Where(x => x.CreatedAt < history && x.DispatchedAt != null && !db.WebhookDeliveries.Any(delivery => delivery.EventId == x.Id) && !db.StaffNotifications.Any(notification => notification.EventId == x.Id)).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
