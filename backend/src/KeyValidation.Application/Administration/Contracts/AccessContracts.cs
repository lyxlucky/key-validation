using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record AccountAccessDto(string[] Permissions, bool AllApplications, Guid[] ApplicationIds, Guid? ChannelId);
public record UpdateAccountAccess(string[]? Permissions, [property: JsonRequired] bool AllApplications, [property: JsonRequired] Guid[] ApplicationIds, Guid? ChannelId = null);
