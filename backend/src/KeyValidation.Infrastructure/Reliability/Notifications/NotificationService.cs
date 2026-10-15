using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class NotificationService(AppDbContext db, ApplicationAccessScope access, RecipientAccessService recipients, IDurableEventPublisher publisher, TimeProvider clock)
{
    public async Task<NotificationPolicyDto> Policy(Guid applicationId)
    {
        access.Require(ApplicationPermissions.Administrator);
        await RequireApplication(applicationId);
        return Dto(await db.NotificationPolicies.AsNoTracking().SingleOrDefaultAsync(x => x.ApplicationId == applicationId) ?? new NotificationPolicy { ApplicationId = applicationId });
    }

    public async Task<NotificationPolicyDto> SetPolicy(Guid applicationId, UpdateNotificationPolicy request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        var types = ReliabilityRules.EventTypes(request.EventTypes);
        if (request.RecipientIds is null || request.RecipientIds.Length > 1000 || request.RecipientIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 450) || request.ExpiryDays is null || request.ExpiryDays.Length > 3 || request.ExpiryDays.Any(x => x is not 3 and not 7 and not 30))
            throw new RuleException("VALIDATION_ERROR", 400);
        var ids = request.RecipientIds.Distinct().Order(StringComparer.Ordinal).ToArray();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 1)");
        await RequireApplication(applicationId);
        if ((await recipients.Eligible(applicationId, ids)).Length != ids.Length)
            throw new RuleException("INVALID_NOTIFICATION_RECIPIENT", 400);
        var policy = await db.NotificationPolicies.SingleOrDefaultAsync(x => x.ApplicationId == applicationId);
        var before = policy is null ? null : Dto(policy);
        if (policy is null)
        {
            policy = new()
            {
                ApplicationId = applicationId
            };
            db.NotificationPolicies.Add(policy);
        }

        policy.Enabled = request.Enabled;
        policy.RecipientIds = ids;
        policy.EventTypes = types;
        policy.ExpiryDays = request.ExpiryDays.Distinct().Order().ToArray();
        ReliabilityRules.Audit(db, actor, "notification-policy.update", applicationId, applicationId, clock.GetUtcNow(), new { before, after = Dto(policy) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(policy);
    }

    public async Task<PagedResult<NotificationDto>> Inbox(string recipientId, int page, int pageSize, bool unreadOnly, Guid? applicationId)
    {
        ReliabilityRules.Page(page, pageSize);
        var query = Visible(recipientId).AsNoTracking().Where(x => (!unreadOnly || x.ReadAt == null) && (applicationId == null || x.ApplicationId == applicationId));
        var count = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(items.Select(Dto).ToArray(), count, page, pageSize);
    }

    public async Task MarkRead(Guid id, string recipientId)
    {
        var permission = access.IsAdministrator ? ApplicationPermissions.Administrator : access.Permissions.FirstOrDefault() ?? throw new RuleException("NOT_FOUND", 404);
        access.RequireWrite(permission);
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_staff_notifications\" WHERE \"Id\" = {id} FOR UPDATE");
        var item = await Visible(recipientId).SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        item.ReadAt ??= clock.GetUtcNow();
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    internal async Task PublishExpirations(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var policies = await db.NotificationPolicies.Where(x => x.Enabled && x.EventTypes.Contains(OperationsEventTypes.LicenseExpiring)).ToArrayAsync(cancellationToken);
        // Webhook-only destinations use the standard stages even without an inbox policy.
        var webhookApps = await db.WebhookSubscriptions.Where(x => x.IsEnabled && x.EventTypes.Contains(OperationsEventTypes.LicenseExpiring)).Select(x => x.ApplicationId).Distinct().ToArrayAsync(cancellationToken);
        foreach (var applicationId in policies.Select(x => x.ApplicationId).Concat(webhookApps).Distinct().Order())
        {
            var stages = policies.SingleOrDefault(x => x.ApplicationId == applicationId)?.ExpiryDays ?? [3, 7, 30];
            if (stages.Length == 0 || !await db.Applications.AnyAsync(x => x.Id == applicationId && x.DisabledAt == null, cancellationToken))
                continue;
            var latest = now.AddDays(stages.Max());
            var licenses = await db.Licenses.AsNoTracking().Where(x => x.ApplicationId == applicationId && x.ExpiresAt > now && x.ExpiresAt <= latest && x.DisabledAt == null && x.FrozenAt == null).Select(x => new { x.Id, x.ChannelId, x.ExpiresAt }).ToArrayAsync(cancellationToken);
            foreach (var license in licenses)
                foreach (var days in stages.Where(days => license.ExpiresAt <= now.AddDays(days)))
                    await publisher.PublishAsync(applicationId, license.Id, OperationsEventTypes.LicenseExpiring, JsonSerializer.Serialize(new { licenseId = license.Id, expiresAt = license.ExpiresAt, days }), $"expiry:{license.Id}:{license.ExpiresAt!.Value.UtcTicks}:{days}", cancellationToken, license.ChannelId);
        }
    }

    internal async Task<(int Events, int Notifications, int Deliveries)> Dispatch(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var events = await db.OperationsEvents.Where(x => x.DispatchedAt == null).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(1000).ToArrayAsync(cancellationToken);
        var notifications = 0;
        var deliveries = 0;
        foreach (var record in events)
        {
            var policy = await db.NotificationPolicies.SingleOrDefaultAsync(x => x.ApplicationId == record.ApplicationId, cancellationToken);
            if (policy is { Enabled: true } && policy.EventTypes.Contains(record.EventType))
                foreach (var recipient in await recipients.Eligible(record.ApplicationId, policy.RecipientIds, cancellationToken, record.ChannelId, forEvent: true))
                {
                    if (await db.StaffNotifications.AnyAsync(x => x.EventId == record.Id && x.RecipientId == recipient, cancellationToken))
                        continue;
                    var title = Title(record.EventType);
                    db.StaffNotifications.Add(new() { EventId = record.Id, ApplicationId = record.ApplicationId, ChannelId = record.ChannelId, RecipientId = recipient, EventType = record.EventType, ResourceId = record.ResourceId, Title = title, Message = Message(record, title), CreatedAt = now });
                    notifications++;
                }

            var subscriptions = await db.WebhookSubscriptions.Where(x => x.ApplicationId == record.ApplicationId && x.IsEnabled && x.EventTypes.Contains(record.EventType)).Select(x => x.Id).ToArrayAsync(cancellationToken);
            foreach (var subscription in subscriptions)
            {
                if (await db.WebhookDeliveries.AnyAsync(x => x.EventId == record.Id && x.SubscriptionId == subscription, cancellationToken))
                    continue;
                db.WebhookDeliveries.Add(new() { ApplicationId = record.ApplicationId, EventId = record.Id, SubscriptionId = subscription, EventType = record.EventType, CreatedAt = now, NextAttemptAt = now, ExpiresAt = now.AddHours(24) });
                deliveries++;
            }

            record.DispatchedAt = now;
        }

        return (events.Length, notifications, deliveries);
    }

    private IQueryable<StaffNotification> Visible(string id) => db.StaffNotifications.Where(x => x.RecipientId == id && (access.IsAdministrator || access.Permissions.Length > 0));
    private async Task RequireApplication(Guid id)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == id))
            throw new RuleException("NOT_FOUND", 404);
    }

    private static NotificationPolicyDto Dto(NotificationPolicy x) => new(x.ApplicationId, x.Enabled, x.RecipientIds, x.EventTypes, x.ExpiryDays);
    private static NotificationDto Dto(StaffNotification x) => new(x.Id, x.ApplicationId, x.EventType, x.Title, x.Message, x.ReadAt, x.CreatedAt, x.ResourceId);
    private static string Title(string type) => type switch
    {
        OperationsEventTypes.LicenseExpiring => "授权即将到期",
        OperationsEventTypes.RiskDetected => "发现异常使用",
        OperationsEventTypes.BatchCompleted => "批量操作完成",
        OperationsEventTypes.ServiceDegraded => "服务异常",
        OperationsEventTypes.ServiceRecovered => "服务恢复",
        OperationsEventTypes.OrderFulfilled => "订单已交付",
        OperationsEventTypes.LicenseRenewed => "授权已续期",
        OperationsEventTypes.BindingReleased => "设备绑定已释放",
        _ => "运营通知"
    };
    private static string Message(OperationsEvent record, string title)
    {
        if (record.EventType == OperationsEventTypes.BatchCompleted)
        {
            using var body = JsonDocument.Parse(record.BodyJson);
            var data = body.RootElement.GetProperty("data");
            var succeeded = data.TryGetProperty("succeeded", out var success) ? success.GetInt32() : 0;
            var failed = data.TryGetProperty("failed", out var failure) ? failure.GetInt32() : 0;
            return $"{title}：当前应用成功 {succeeded}，失败 {failed}。引用 {record.ResourceId}";
        }

        return $"{title}。引用 {record.ResourceId}";
    }
}
