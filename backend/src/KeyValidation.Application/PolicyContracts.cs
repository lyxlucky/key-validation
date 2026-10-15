namespace KeyValidation.Application;

public record FreezeLicense(string Reason);
public record LicenseFreezeDto(Guid Id, Guid LicenseId, DateTimeOffset FrozenAt, DateTimeOffset? UnfrozenAt, double RemainingSeconds, string FrozenBy, string? UnfrozenBy, string FreezeReason, string? UnfreezeReason);
public record TrialPolicyDto(Guid ApplicationId, bool IsEnabled, long DurationSeconds, string[] Features);
public record UpdateTrialPolicy([property: System.Text.Json.Serialization.JsonRequired] bool IsEnabled, long DurationSeconds, string[]? Features = null);
public record ActivateTrial(string AppCode, string DeviceId, string? DeviceLabel = null, string? ClientVersion = null);
