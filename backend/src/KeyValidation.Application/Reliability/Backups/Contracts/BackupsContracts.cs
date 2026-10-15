namespace KeyValidation.Application;
public record BackupStatusDto(bool Enabled, bool Configured, bool ScheduleReady, int IntervalHours, int RetentionCount, long MaxArchiveBytes, string[] ConfigurationErrors, DateTimeOffset? LastSuccessfulAt, DateTimeOffset? NextScheduledAt);
public record BackupJobDto(Guid Id, string Status, DateTimeOffset CreatedAt, DateTimeOffset? CompletedAt, long? SizeBytes, string? Sha256, DateTimeOffset? VerifiedAt, string VerificationStatus, long? VerificationDurationMilliseconds, string? ErrorCode, string? VerificationErrorCode);
public record BackupVerificationDto(Guid BackupId, string Status, DateTimeOffset CompletedAt, long DurationMilliseconds, int MigrationCount, bool KeysReadable, string? ErrorCode);
public record IsolatedRestoreDto(Guid BackupId, string DatabaseName, string KeysDirectory, int MigrationCount, long DurationMilliseconds);
