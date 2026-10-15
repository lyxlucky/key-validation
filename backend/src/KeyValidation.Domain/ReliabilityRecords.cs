namespace KeyValidation.Domain;

public sealed class RiskPolicy
{
    public Guid ApplicationId { get; set; }
    public bool Enabled { get; set; } = true;
    public int ActivationThreshold { get; set; } = 10;
    public int ActivationWindowMinutes { get; set; } = 10;
    public int RebindingThreshold { get; set; } = 3;
    public int RebindingWindowMinutes { get; set; } = 60;
    public int RegionWindowHours { get; set; } = 24;
    public DateTimeOffset? ScannedThrough { get; set; }
}

public sealed class RiskEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid LicenseId { get; set; }
    public string Rule { get; set; } = "";
    public string Summary { get; set; } = "";
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public string Status { get; set; } = "open";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public string? ReviewReason { get; set; }
}

public sealed class NotificationPolicy
{
    public Guid ApplicationId { get; set; }
    public bool Enabled { get; set; }
    public string[] RecipientIds { get; set; } = [];
    public string[] EventTypes { get; set; } = [];
    public int[] ExpiryDays { get; set; } = [3, 7, 30];
}

public sealed class OperationsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid ResourceId { get; set; }
    public string EventType { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string BodyJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}

public sealed class StaffNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string RecipientId { get; set; } = "";
    public string EventType { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public sealed class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string[] EventTypes { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string ProtectedSecret { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class WebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid EventId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string EventType { get; set; } = "";
    public string Status { get; set; } = "pending";
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Guid? LeaseId { get; set; }
    public DateTimeOffset? LeaseExpiresAt { get; set; }
}

public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "running";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string VerificationStatus { get; set; } = "not_verified";
    public DateTimeOffset? VerifiedAt { get; set; }
    public long? VerificationDurationMilliseconds { get; set; }
    public string? ErrorCode { get; set; }
    public string? VerificationErrorCode { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}

public sealed class MonitoringSample
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InstanceId { get; set; } = "";
    public DateTimeOffset SampledAt { get; set; }
    public DateTimeOffset WindowStart { get; set; }
    public DateTimeOffset WindowEnd { get; set; }
    public bool DatabaseAvailable { get; set; }
    public long RequestCount { get; set; }
    public long ServerErrorCount { get; set; }
    public double? ErrorRate { get; set; }
    public double? P95LatencyMs { get; set; }
    public string RoutesJson { get; set; } = "[]";
}

public sealed class MonitoringIncident
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "";
    public string Summary { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}
