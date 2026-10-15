using System.Text.Json.Serialization;

namespace KeyValidation.Application;

public record CustomerDto(Guid Id, Guid ApplicationId, Guid? ChannelId, string? Name, string? Email, string? Phone, string? Note,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DisabledAt, DateTimeOffset? AnonymizedAt, bool HasAccessCode);
public record CreateCustomer(Guid ApplicationId, string Name, string? Email = null, string? Phone = null, string? Note = null, Guid? ChannelId = null, Guid? RequestId = null);
public record UpdateCustomer(string Name, string? Email = null, string? Phone = null, string? Note = null);
public record ExternalUpdateCustomer(Guid RequestId, string Name, string? Email = null, string? Phone = null, string? Note = null);
public record CustomerAccessCodeDto(CustomerDto Customer, string AccessCode);

public record CreateOrder(Guid RequestId, Guid ApplicationId, Guid CustomerId, Guid PlanId, string Type, int Quantity,
    long UnitPriceCents = 0, Guid? LicenseId = null, long? AdditionalSeconds = null);
public record FulfillOrder(Guid RequestId);
public record OrderSnapshotDto(string PlanName, long DurationSeconds, int MaxDevices, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features);
public record OrderHistoryDto(string Action, string ActorId, DateTimeOffset CreatedAt);
public record OrderDto(Guid Id, Guid ApplicationId, Guid? ChannelId, Guid CustomerId, Guid PlanId, string Type, string Status,
    int Quantity, long UnitPriceCents, long TotalCents, string Currency, Guid RequestId, Guid? RenewalLicenseId, Guid? BatchId,
    OrderSnapshotDto Snapshot, string CreatedBy, DateTimeOffset CreatedAt, string? FulfilledBy, DateTimeOffset? FulfilledAt,
    DateTimeOffset? CancelledAt, bool DeliveryAvailable, DateTimeOffset? DeliveryExpiresAt, Guid[] LicenseIds, IReadOnlyList<OrderHistoryDto> History);
public record OrderDeliveryDto(Guid OrderId, DateTimeOffset ExpiresAt, IReadOnlyList<IssuedKey> Licenses);
public record OrderCustomerOption(Guid Id, Guid ApplicationId, Guid? ChannelId, string? Name);
public record OrderLicenseOption(Guid Id, Guid CustomerId, string KeyHint);
public record OrderCatalogDto(IReadOnlyList<OrderCustomerOption> Customers, IReadOnlyList<OrderLicenseOption> Licenses);

public record CreateChannel(string Name, string AccountId, Guid[] ApplicationIds, long InitialQuota = 0);
public record UpdateChannel(string Name, string AccountId, Guid[] ApplicationIds, [property: JsonRequired] bool IsEnabled);
public record AdjustChannelQuota(Guid RequestId, long Delta, string Reason);
public record SetChannelPrice(Guid ApplicationId, Guid PlanId, long UnitPriceCents);
public record ChannelPriceDto(Guid ApplicationId, Guid PlanId, long UnitPriceCents);
public record ChannelDto(Guid Id, string Name, string AccountId, Guid[] ApplicationIds, long RemainingQuota, bool IsEnabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, IReadOnlyList<ChannelPriceDto> Prices);
public record ChannelQuotaDto(Guid Id, Guid ChannelId, string Kind, long Delta, long BalanceAfter, string Reason, string ActorId,
    DateTimeOffset CreatedAt, Guid? OrderId, Guid RequestId);

public record CreatePortalSession(string AccessCode);
public record PortalSessionDto(string Token, DateTimeOffset ExpiresAt, CustomerDto Customer);
public record CreatePortalRequest(Guid RequestId, string Type, Guid LicenseId, Guid? BindingId = null, string? Note = null);
public record ReviewPortalRequest(string Status, string Reason, Guid? PlanId = null, long? UnitPriceCents = null, long? AdditionalSeconds = null);
public record PortalRequestDto(Guid Id, Guid ApplicationId, Guid? ChannelId, Guid CustomerId, string Type, Guid LicenseId, Guid? BindingId,
    string? Note, string Status, Guid RequestId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy, string? ReviewReason, Guid? OrderId);

public record CreateApiCredential(Guid ApplicationId, string Name, string[] Permissions, Guid? ChannelId = null);
public record UpdateApiCredential(string Name, string[] Permissions, [property: JsonRequired] bool IsEnabled);
public record ApiCredentialDto(Guid Id, Guid ApplicationId, Guid? ChannelId, string Name, string[] Permissions, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ApiCredentialSecretDto(ApiCredentialDto Credential, string Secret);

public interface IOrderDeliveryProtection
{
    string Protect(Guid orderId, IReadOnlyList<IssuedKey> keys);
    IReadOnlyList<IssuedKey> Unprotect(Guid orderId, string ciphertext);
}
