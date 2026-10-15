namespace KeyValidation.Domain;
public sealed class BackupJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "running";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public long? SizeBytes { get; set; }
    public string? Sha256 { get; set; }
    public string VerificationStatus { get; set; } = "not_verified";
    public DateTimeOffset? VerifiedAt { get; set; }
    public long? VerificationDurationMilliseconds { get; set; }
    public string? ErrorCode { get; set; }
    public string? VerificationErrorCode { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
}
