using System.Text.Json.Serialization;

namespace KeyValidation.Application;

public record DevicePolicyDto(Guid LicenseId, bool Enabled, int MaxChanges, int WindowDays, int CooldownHours,
    int UsedChanges, int? RemainingChanges, DateTimeOffset? LastChangedAt, DateTimeOffset? NextAllowedAt, DateTimeOffset ServerTime);
public record UpdateDevicePolicy([property: JsonRequired] bool Enabled, [property: JsonRequired] int MaxChanges,
    [property: JsonRequired] int WindowDays, [property: JsonRequired] int CooldownHours, [property: JsonRequired] string Reason);
public record ResetDevicePolicy([property: JsonRequired] string Reason);
