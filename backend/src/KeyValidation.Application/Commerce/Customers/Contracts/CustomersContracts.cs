using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record CustomerDto(Guid Id, Guid ApplicationId, Guid? ChannelId, string? Name, string? Email, string? Phone, string? Note, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? DisabledAt, DateTimeOffset? AnonymizedAt, bool HasAccessCode);
public record CreateCustomer(Guid ApplicationId, string Name, string? Email = null, string? Phone = null, string? Note = null, Guid? ChannelId = null, Guid? RequestId = null);
public record UpdateCustomer(string Name, string? Email = null, string? Phone = null, string? Note = null);
public record ExternalUpdateCustomer(Guid RequestId, string Name, string? Email = null, string? Phone = null, string? Note = null);
public record CustomerAccessCodeDto(CustomerDto Customer, string AccessCode);
