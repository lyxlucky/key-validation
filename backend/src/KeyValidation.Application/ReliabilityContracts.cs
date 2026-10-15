namespace KeyValidation.Application;

public static class OperationsEventTypes
{
    public const string LicenseExpiring = "license.expiring";
    public const string RiskDetected = "risk.detected";
    public const string BatchCompleted = "batch.completed";
    public const string ServiceDegraded = "service.degraded";
    public const string ServiceRecovered = "service.recovered";
    public const string OrderFulfilled = "order.fulfilled";
    public const string LicenseRenewed = "license.renewed";
    public const string BindingReleased = "binding.released";
    public static readonly string[] All = [LicenseExpiring, RiskDetected, BatchCompleted, ServiceDegraded, ServiceRecovered, OrderFulfilled, LicenseRenewed, BindingReleased];
}

// The caller owns its existing transaction and SaveChanges. Publishing does not
// commit; duplicate keys return the existing stable event ID within that transaction.
public interface IDurableEventPublisher
{
    Task<Guid> PublishAsync(Guid applicationId, Guid resourceId, string eventType, string safePayloadJson,
        string deduplicationKey, CancellationToken cancellationToken = default, Guid? channelId = null);
}

public interface IWebhookDeliveryProcessor
{
    Task<int> DeliverDueAsync(CancellationToken cancellationToken = default);
}

public interface IValidationRequestMetrics
{
    void Record(string? route, int statusCode, double elapsedMilliseconds);
}

public interface IDatabaseAvailabilityProbe
{
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

public sealed class ClientRequestFacts { public string? TrustedCountryCode { get; set; } }

public record RiskPolicyDto(Guid ApplicationId, bool Enabled, int ActivationThreshold, int ActivationWindowMinutes,
    int RebindingThreshold, int RebindingWindowMinutes, int RegionWindowHours);
public record UpdateRiskPolicy(bool Enabled, int ActivationThreshold = 10, int ActivationWindowMinutes = 10,
    int RebindingThreshold = 3, int RebindingWindowMinutes = 60, int RegionWindowHours = 24);
public record RiskEventDto(Guid Id, Guid ApplicationId, Guid LicenseId, string Rule, string Summary, string EvidenceJson,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string Status, DateTimeOffset CreatedAt,
    DateTimeOffset? ReviewedAt, string? ReviewedBy, string? ReviewReason);
public record ReviewRiskEvent(string Status, string Reason);
public record OperationsScanDto(DateTimeOffset ScannedAt, int RiskEventsCreated, int EventsDispatched, int NotificationsCreated, int DeliveriesCreated);

public record NotificationPolicyDto(Guid ApplicationId, bool Enabled, string[] RecipientIds, string[] EventTypes, int[] ExpiryDays);
public record UpdateNotificationPolicy(bool Enabled, string[] RecipientIds, string[] EventTypes, int[] ExpiryDays);
public record NotificationDto(Guid Id, Guid ApplicationId, string EventType, string Title, string Message,
    DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid ResourceId);

public record CreateWebhookSubscription(Guid ApplicationId, string Name, string Url, string[] EventTypes, bool IsEnabled = true);
public record UpdateWebhookSubscription(string Name, string Url, string[] EventTypes, bool IsEnabled);
public record WebhookSubscriptionDto(Guid Id, Guid ApplicationId, string Name, string Url, string[] EventTypes,
    bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record WebhookSecretDto(WebhookSubscriptionDto Subscription, string Secret);
public record WebhookDeliveryDto(Guid Id, Guid EventId, Guid SubscriptionId, string EventType, string Status, int Attempts,
    DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt, string? LastError, DateTimeOffset CreatedAt);

public record BackupStatusDto(bool Enabled, bool Configured, bool ScheduleReady, int IntervalHours, int RetentionCount,
    long MaxArchiveBytes, string[] ConfigurationErrors, DateTimeOffset? LastSuccessfulAt, DateTimeOffset? NextScheduledAt);
public record BackupJobDto(Guid Id, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, long? SizeBytes,
    string? Sha256, DateTimeOffset? VerifiedAt, string VerificationStatus, long? VerificationDurationMilliseconds,
    string? ErrorCode, string? VerificationErrorCode);
public record BackupVerificationDto(Guid BackupId, string Status, DateTimeOffset CompletedAt, long DurationMilliseconds,
    int MigrationCount, bool KeysReadable, string? ErrorCode);
public record IsolatedRestoreDto(Guid BackupId, string DatabaseName, string KeysDirectory, int MigrationCount, long DurationMilliseconds);

public record MonitoringRouteDto(string Route, long RequestCount, long ServerErrorCount, double? ErrorRate, double? P95LatencyMs);
public record MonitoringSampleDto(Guid Id, DateTimeOffset SampledAt, DateTimeOffset WindowStart, DateTimeOffset WindowEnd,
    bool DatabaseAvailable, long RequestCount, long ServerErrorCount, double? ErrorRate, double? P95LatencyMs, MonitoringRouteDto[] Routes);
public record MonitoringIncidentDto(Guid Id, string Kind, string Summary, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt);
public record WorkerErrorDto(string Worker, string ErrorCode, DateTimeOffset OccurredAt);
public record MonitoringDto(DateTimeOffset GeneratedAt, bool DatabaseAvailable, MonitoringSampleDto? Latest,
    MonitoringSampleDto[] Samples, MonitoringIncidentDto[] Incidents, WorkerErrorDto[] WorkerErrors);
