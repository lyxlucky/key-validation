using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record CreatePortalSession(string AccessCode);
public record PortalSessionDto(string Token, DateTimeOffset ExpiresAt, CustomerDto Customer);
public record CreatePortalRequest(Guid RequestId, string Type, Guid LicenseId, Guid? BindingId = null, string? Note = null);
public record ReviewPortalRequest(string Status, string Reason, Guid? PlanId = null, long? UnitPriceCents = null, long? AdditionalSeconds = null);
public record PortalRequestDto(Guid Id, Guid ApplicationId, Guid? ChannelId, Guid CustomerId, string Type, Guid LicenseId, Guid? BindingId, string? Note, string Status, Guid RequestId, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy, string? ReviewReason, Guid? OrderId);
