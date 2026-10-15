using KeyValidation.Domain;

namespace KeyValidation.Application;

public sealed record UpdateVersionPolicy(string? MinimumVersion, string? LatestVersion, bool EnforceMinimum,
    string? UpdateUrl = null, string? ReleaseNotes = null, string? Sha256 = null);
public sealed record VersionPolicyDto(Guid ApplicationId, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum,
    string? UpdateUrl, string? ReleaseNotes, string? Sha256);
public sealed record PublicVersionDto(string ApplicationCode, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum,
    string? UpdateUrl, string? ReleaseNotes, string? Sha256);
public sealed record ClientVersionInfo(string? ClientVersion, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum,
    bool UpgradeAvailable, string? UpdateUrl, string? ReleaseNotes, string? Sha256);
public sealed record ClientVersionOptions(bool AllowLoopbackHttp = false);
public sealed class ClientVersionException(string code, ClientVersionInfo version) : RuleException(code, 426)
{
    public ClientVersionInfo Version { get; } = version;
}

public sealed record SignOfflineLicense(Guid LicenseId, Guid BindingId, string DeviceId, int? Hours = null);
public sealed record VerifyOfflineLicense(string LicenseContent, string AppCode, string DeviceId);
public sealed record OfflineLicensePayload(int FormatVersion, Guid Serial, string ApplicationCode, Guid LicenseId, Guid BindingId,
    string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, string DeviceHash);
public sealed record OfflineLicenseDto(Guid Id, Guid Serial, Guid ApplicationId, string ApplicationCode, Guid LicenseId, string LicenseHint,
    Guid BindingId, string? DeviceLabel, string Kid, string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, string? RevocationReason, string IssuedBy, string Status, string Limitations);
public sealed record SignedOfflineLicenseDto(OfflineLicenseDto Record, string LicenseContent);
public sealed record VerifiedOfflineLicenseDto(Guid Serial, string ApplicationCode, Guid LicenseId, Guid BindingId, string[] Features,
    DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, DateTimeOffset ServerTime, string Status, string Limitations);
public sealed record OfflineConfigurationDto(bool Enabled, bool Configured, string? CurrentKeyId, IReadOnlyDictionary<string, string> PublicKeys,
    string? ErrorCode, int MaxHours, int DefaultHours, string Limitations);
public interface IOfflineLicenseSigner
{
    OfflineConfigurationDto Configuration();
    (string Content, string KeyId) Sign(OfflineLicensePayload payload);
    OfflineLicensePayload Verify(string content);
}
public static class OfflineLicenseLimits
{
    public const int MaximumHours = 168;
    public const int DefaultHours = 24;
    public const string Limitations = "离线设备无法即时获知撤销、冻结、禁用或解绑；最长延迟为许可证剩余有效期。";
}
