using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record CreateOrder(Guid RequestId, Guid ApplicationId, Guid CustomerId, Guid PlanId, string Type, int Quantity, long UnitPriceCents = 0, Guid? LicenseId = null, long? AdditionalSeconds = null);
public record FulfillOrder(Guid RequestId);
public record OrderSnapshotDto(string PlanName, long DurationSeconds, int MaxDevices, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features);
public record OrderHistoryDto(string Action, string ActorId, DateTimeOffset CreatedAt);
public record OrderDto(Guid Id, Guid ApplicationId, Guid? ChannelId, Guid CustomerId, Guid PlanId, string Type, string Status, int Quantity, long UnitPriceCents, long TotalCents, string Currency, Guid RequestId, Guid? RenewalLicenseId, Guid? BatchId, OrderSnapshotDto Snapshot, string CreatedBy, DateTimeOffset CreatedAt, string? FulfilledBy, DateTimeOffset? FulfilledAt, DateTimeOffset? CancelledAt, bool DeliveryAvailable, DateTimeOffset? DeliveryExpiresAt, Guid[] LicenseIds, IReadOnlyList<OrderHistoryDto> History);
public record OrderDeliveryDto(Guid OrderId, DateTimeOffset ExpiresAt, IReadOnlyList<IssuedKey> Licenses);
public record OrderCustomerOption(Guid Id, Guid ApplicationId, Guid? ChannelId, string? Name);
public record OrderLicenseOption(Guid Id, Guid CustomerId, string KeyHint);
public record OrderCatalogDto(IReadOnlyList<OrderCustomerOption> Customers, IReadOnlyList<OrderLicenseOption> Licenses);
