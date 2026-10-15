using KeyValidation.Domain;

namespace KeyValidation.Application;
public sealed record UpdateVersionPolicy(string? MinimumVersion, string? LatestVersion, bool EnforceMinimum, string? UpdateUrl = null, string? ReleaseNotes = null, string? Sha256 = null);
public sealed record VersionPolicyDto(Guid ApplicationId, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum, string? UpdateUrl, string? ReleaseNotes, string? Sha256);
public sealed record PublicVersionDto(string ApplicationCode, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum, string? UpdateUrl, string? ReleaseNotes, string? Sha256);
public sealed record ClientVersionInfo(string? ClientVersion, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum, bool UpgradeAvailable, string? UpdateUrl, string? ReleaseNotes, string? Sha256);
