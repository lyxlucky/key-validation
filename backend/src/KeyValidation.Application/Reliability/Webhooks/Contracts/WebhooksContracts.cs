namespace KeyValidation.Application;
public record CreateWebhookSubscription(Guid ApplicationId, string Name, string Url, string[] EventTypes, bool IsEnabled = true);
public record UpdateWebhookSubscription(string Name, string Url, string[] EventTypes, bool IsEnabled);
public record WebhookSubscriptionDto(Guid Id, Guid ApplicationId, string Name, string Url, string[] EventTypes, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record WebhookSecretDto(WebhookSubscriptionDto Subscription, string Secret);
public record WebhookDeliveryDto(Guid Id, Guid EventId, Guid SubscriptionId, string EventType, string Status, int Attempts, DateTimeOffset? NextAttemptAt, DateTimeOffset? CompletedAt, string? LastError, DateTimeOffset CreatedAt);
