using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record CreateApiCredential(Guid ApplicationId, string Name, string[] Permissions, Guid? ChannelId = null);
public record UpdateApiCredential(string Name, string[] Permissions, [property: JsonRequired] bool IsEnabled);
public record ApiCredentialDto(Guid Id, Guid ApplicationId, Guid? ChannelId, string Name, string[] Permissions, bool IsEnabled, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ApiCredentialSecretDto(ApiCredentialDto Credential, string Secret);
