namespace KeyValidation.Domain;

public sealed class ApplicationVersionPolicy
{
    public Guid ApplicationId { get; set; }
    public string? MinimumVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool EnforceMinimum { get; set; }
    public string? UpdateUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? Sha256 { get; set; }
}

public sealed class OfflineLicenseRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid Serial { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid LicenseId { get; set; }
    public Guid BindingId { get; set; }
    public string KeyId { get; set; } = "";
    public string[] Features { get; set; } = [];
    public string ContentSha256 { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset NotBefore { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public string IssuedBy { get; set; } = "";
}
