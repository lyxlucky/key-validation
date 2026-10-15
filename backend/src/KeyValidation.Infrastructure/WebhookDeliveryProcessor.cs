using System.Security.Cryptography;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class WebhookDeliveryProcessor(AppDbContext db, WebhookTransport transport, IDataProtectionProvider protection, TimeProvider clock) : IWebhookDeliveryProcessor
{
    private readonly IDataProtector protector = protection.CreateProtector("KeyValidation.Webhook.Secret.v1");
    private static readonly int[] RetryMinutes = [1, 5, 30, 120];

    public async Task<int> DeliverDueAsync(CancellationToken cancellationToken = default)
    {
        var attempts = 0;
        for (var index = 0; index < 100; index++)
        {
            db.ChangeTracker.Clear();
            var claimed = await Claim(cancellationToken);
            if (claimed is null) break;
            if (claimed.Status != "delivering") continue;
            attempts++;
            var subscription = await db.WebhookSubscriptions.AsNoTracking().SingleAsync(x => x.Id == claimed.SubscriptionId, cancellationToken);
            var record = await db.OperationsEvents.AsNoTracking().SingleAsync(x => x.Id == claimed.EventId, cancellationToken);
            string? error;
            if (!subscription.IsEnabled) error = "SUBSCRIPTION_DISABLED";
            else
            {
                byte[]? secret = null;
                try
                {
                    secret = Convert.FromBase64String(protector.Unprotect(subscription.ProtectedSecret));
                    error = await transport.SendAsync(subscription.Url, secret, record.Id, record.BodyJson, cancellationToken);
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException) { error = "SECRET_UNAVAILABLE"; }
                finally { if (secret is not null) CryptographicOperations.ZeroMemory(secret); }
            }
            await Complete(claimed, error, cancellationToken);
        }
        return attempts;
    }

    private async Task<WebhookDelivery?> Claim(CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 2)", cancellationToken);
        var now = clock.GetUtcNow();
        var record = await db.WebhookDeliveries.Where(x => x.Status == "pending" && x.NextAttemptAt <= now || x.Status == "delivering" && x.LeaseExpiresAt <= now)
            .OrderBy(x => x.NextAttemptAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefaultAsync(cancellationToken);
        if (record is null) return null;
        if (record.ExpiresAt <= now || record.Attempts >= 5)
        {
            record.Status = record.ExpiresAt <= now ? "expired" : "failed";
            record.LastError = record.ExpiresAt <= now ? "DELIVERY_EXPIRED" : "ATTEMPTS_EXHAUSTED";
            record.CompletedAt = now; record.NextAttemptAt = null; record.LeaseId = null; record.LeaseExpiresAt = null;
        }
        else
        {
            record.Status = "delivering"; record.Attempts++; record.LeaseId = Guid.NewGuid(); record.LeaseExpiresAt = now.AddMinutes(2);
        }
        await db.SaveChangesAsync(cancellationToken); await tx.CommitAsync(cancellationToken); return record;
    }

    private async Task Complete(WebhookDelivery claim, string? error, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var terminal = error is null || claim.Attempts >= 5 || claim.ExpiresAt <= now || error == "SUBSCRIPTION_DISABLED";
        var status = error is null ? "succeeded" : claim.ExpiresAt <= now ? "expired" : terminal ? "failed" : "pending";
        DateTimeOffset? next = terminal ? null : now.AddMinutes(RetryMinutes[claim.Attempts - 1]);
        DateTimeOffset? completed = terminal ? now : null;
        // A stale process cannot complete a newer lease. A crash after delivery
        // and before this write may duplicate the stable event on a later retry.
        await db.WebhookDeliveries.Where(x => x.Id == claim.Id && x.Status == "delivering" && x.LeaseId == claim.LeaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.Status, status).SetProperty(x => x.LastError, error)
                .SetProperty(x => x.NextAttemptAt, next).SetProperty(x => x.CompletedAt, completed)
                .SetProperty(x => x.LeaseId, (Guid?)null).SetProperty(x => x.LeaseExpiresAt, (DateTimeOffset?)null), cancellationToken);
    }
}
