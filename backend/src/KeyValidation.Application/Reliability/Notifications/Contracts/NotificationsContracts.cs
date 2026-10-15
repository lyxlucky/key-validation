namespace KeyValidation.Application;
public record NotificationPolicyDto(Guid ApplicationId, bool Enabled, string[] RecipientIds, string[] EventTypes, int[] ExpiryDays);
public record UpdateNotificationPolicy(bool Enabled, string[] RecipientIds, string[] EventTypes, int[] ExpiryDays);
public record NotificationDto(Guid Id, Guid ApplicationId, string EventType, string Title, string Message, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt, Guid ResourceId);
