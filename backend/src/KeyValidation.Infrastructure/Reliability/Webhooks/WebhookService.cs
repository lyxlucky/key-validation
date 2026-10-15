using System.Security.Cryptography;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class WebhookService(AppDbContext db, ApplicationAccessScope access, WebhookTargetPolicy targets, IDataProtectionProvider protection, TimeProvider clock)
{
    private readonly IDataProtector protector = protection.CreateProtector("KeyValidation.Webhook.Secret.v1");
    public async Task<PagedResult<WebhookSubscriptionDto>> Subscriptions(int page, int pageSize, Guid? applicationId)
    {
        access.Require(ApplicationPermissions.ManageIntegrations);
        ReliabilityRules.Page(page, pageSize);
        var query = db.WebhookSubscriptions.AsNoTracking().Where(x => applicationId == null || x.ApplicationId == applicationId);
        var count = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(items.Select(Dto).ToArray(), count, page, pageSize);
    }

    public async Task<WebhookSecretDto> Create(CreateWebhookSubscription request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageIntegrations);
        var types = Validate(request.Name, request.Url, request.EventTypes);
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await db.Applications.AnyAsync(x => x.Id == request.ApplicationId))
            throw new RuleException("NOT_FOUND", 404);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var now = clock.GetUtcNow();
        var record = new WebhookSubscription
        {
            ApplicationId = request.ApplicationId,
            Name = request.Name.Trim(),
            Url = request.Url,
            EventTypes = types,
            IsEnabled = request.IsEnabled,
            ProtectedSecret = protector.Protect(secret),
            CreatedAt = now,
            UpdatedAt = now
        };
        db.WebhookSubscriptions.Add(record);
        ReliabilityRules.Audit(db, actor, "webhook.create", record.Id, record.ApplicationId, now, new { before = (object? )null, after = AuditFacts(record) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(Dto(record), secret);
    }

    public async Task<WebhookSubscriptionDto> Update(Guid id, UpdateWebhookSubscription request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageIntegrations);
        var types = Validate(request.Name, request.Url, request.EventTypes);
        await using var tx = await db.Database.BeginTransactionAsync();
        var record = await LockSubscription(id);
        var before = AuditFacts(record);
        record.Name = request.Name.Trim();
        record.Url = request.Url;
        record.EventTypes = types;
        record.IsEnabled = request.IsEnabled;
        record.UpdatedAt = clock.GetUtcNow();
        ReliabilityRules.Audit(db, actor, "webhook.update", id, record.ApplicationId, clock.GetUtcNow(), new { before, after = AuditFacts(record) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(record);
    }

    public async Task<WebhookSecretDto> Rotate(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageIntegrations);
        await using var tx = await db.Database.BeginTransactionAsync();
        var record = await LockSubscription(id);
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        record.ProtectedSecret = protector.Protect(secret);
        record.UpdatedAt = clock.GetUtcNow();
        ReliabilityRules.Audit(db, actor, "webhook.rotate-secret", id, record.ApplicationId, clock.GetUtcNow());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(Dto(record), secret);
    }

    public async Task<PagedResult<WebhookDeliveryDto>> Deliveries(int page, int pageSize, Guid? subscriptionId, string? status, Guid? applicationId)
    {
        access.Require(ApplicationPermissions.ManageIntegrations);
        ReliabilityRules.Page(page, pageSize);
        if (status is not null && status is not "pending" and not "delivering" and not "succeeded" and not "failed" and not "expired")
            throw new RuleException("VALIDATION_ERROR", 400);
        if (subscriptionId is not null && !await db.WebhookSubscriptions.AnyAsync(x => x.Id == subscriptionId))
            throw new RuleException("NOT_FOUND", 404);
        var query = db.WebhookDeliveries.AsNoTracking().Where(x => (subscriptionId == null || x.SubscriptionId == subscriptionId) && (status == null || x.Status == status) && (applicationId == null || x.ApplicationId == applicationId));
        var count = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(items.Select(Dto).ToArray(), count, page, pageSize);
    }

    public async Task<WebhookDeliveryDto> Retry(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageIntegrations);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 2)");
        var record = await db.WebhookDeliveries.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        if (record.Status == "delivering" && record.LeaseExpiresAt > clock.GetUtcNow())
            throw new RuleException("DELIVERY_IN_PROGRESS", 409);
        record.Status = "pending";
        record.Attempts = 0;
        record.NextAttemptAt = clock.GetUtcNow();
        record.ExpiresAt = clock.GetUtcNow().AddHours(24);
        record.CompletedAt = null;
        record.LeaseId = null;
        record.LeaseExpiresAt = null;
        record.LastError = null;
        ReliabilityRules.Audit(db, actor, "webhook.retry", id, record.ApplicationId, clock.GetUtcNow());
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(record);
    }

    private string[] Validate(string name, string url, string[] types)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new RuleException("VALIDATION_ERROR", 400);
        targets.Validate(url);
        return ReliabilityRules.EventTypes(types);
    }

    private async Task<WebhookSubscription> LockSubscription(Guid id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_webhook_subscriptions\" WHERE \"Id\" = {id} FOR UPDATE");
        return await db.WebhookSubscriptions.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    }

    private static WebhookSubscriptionDto Dto(WebhookSubscription x) => new(x.Id, x.ApplicationId, x.Name, x.Url, x.EventTypes, x.IsEnabled, x.CreatedAt, x.UpdatedAt);
    private static object AuditFacts(WebhookSubscription x) => new
    {
        x.Name,
        x.IsEnabled,
        EventTypes = x.EventTypes.ToArray()
    };
    internal static WebhookDeliveryDto Dto(WebhookDelivery x) => new(x.Id, x.EventId, x.SubscriptionId, x.EventType, x.Status, x.Attempts, x.NextAttemptAt, x.CompletedAt, x.LastError, x.CreatedAt);
}
