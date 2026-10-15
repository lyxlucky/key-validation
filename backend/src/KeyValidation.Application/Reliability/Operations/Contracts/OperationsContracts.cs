namespace KeyValidation.Application;
public record OperationsScanDto(DateTimeOffset ScannedAt, int RiskEventsCreated, int EventsDispatched, int NotificationsCreated, int DeliveriesCreated);
