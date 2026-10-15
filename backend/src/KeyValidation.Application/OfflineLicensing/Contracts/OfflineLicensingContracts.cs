using KeyValidation.Domain;

namespace KeyValidation.Application;
public sealed record SignOfflineLicense(Guid LicenseId, Guid BindingId, string DeviceId, int? Hours = null);
public sealed record VerifyOfflineLicense(string LicenseContent, string AppCode, string DeviceId);
public sealed record OfflineLicensePayload(int FormatVersion, Guid Serial, string ApplicationCode, Guid LicenseId, Guid BindingId, string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, string DeviceHash);
public sealed record OfflineLicenseDto(Guid Id, Guid Serial, Guid ApplicationId, string ApplicationCode, Guid LicenseId, string LicenseHint, Guid BindingId, string? DeviceLabel, string Kid, string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string? RevocationReason, string IssuedBy, string Status, string Limitations);
public sealed record SignedOfflineLicenseDto(OfflineLicenseDto Record, string LicenseContent);
public sealed record VerifiedOfflineLicenseDto(Guid Serial, string ApplicationCode, Guid LicenseId, Guid BindingId, string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, string Status, string Limitations);
public sealed record OfflineConfigurationDto(bool Enabled, bool Configured, string? CurrentKeyId, IReadOnlyDictionary<string, string> PublicKeys, string? ErrorCode, int MaxHours, int DefaultHours, string Limitations);
