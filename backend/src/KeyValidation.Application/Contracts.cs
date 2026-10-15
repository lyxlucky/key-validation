namespace KeyValidation.Application;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public record PageQuery(int Page = 1, int PageSize = 20);
public record ApplicationDto(Guid Id, string Code, string Name, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt);
public record CreateApplication(string Code, string Name);
public record PlanDto(Guid Id, Guid ApplicationId, string Name, long DurationSeconds, int MaxDevices, bool IsEnabled, DateTimeOffset CreatedAt, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features);
public record CreatePlan(string Name, long DurationSeconds, int MaxDevices = 1, int? MaxConcurrentSessions = null, int? ActivationValidityDays = null, string[]? Features = null);
public record UpdatePlan(string Name, long DurationSeconds, int MaxDevices, [property: System.Text.Json.Serialization.JsonRequired] bool IsEnabled, int? MaxConcurrentSessions = null, int? ActivationValidityDays = null, string[]? Features = null);
public record IssueBatch(Guid ApplicationId, Guid PlanId, int Quantity, string? Note, Guid RequestId);
public record BatchDto(Guid Id, Guid ApplicationId, Guid PlanId, int Quantity, string CreatedBy, DateTimeOffset CreatedAt, string? Note, Guid RequestId);
public record IssuedKey(Guid Id, string Key, string KeyHint);
public record IssuanceDto(BatchDto Batch, IReadOnlyList<IssuedKey> Licenses);
public record LicenseDto(Guid Id, Guid ApplicationId, Guid? BatchId, string KeyHint, long DurationSeconds, int MaxDevices, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ActivatedAt, DateTimeOffset? ExpiresAt, DateTimeOffset? DisabledAt, string? Note, string[] Tags, int MaxConcurrentSessions, DateTimeOffset? ActivateBefore, DateTimeOffset? FrozenAt, double? FrozenRemainingSeconds, string[] Features, bool IsTrial);
public record BatchOverviewDto(Guid Id, Guid ApplicationId, Guid PlanId, int Quantity, string CreatedBy, DateTimeOffset CreatedAt, string? Note, Guid RequestId, string ApplicationName, string PlanName, string? CreatedByName, int ActivatedCount, int DisabledCount);
public record ExpiringLicensesDto(IReadOnlyList<LicenseDto> Items, int Total, int Page, int PageSize, DateTimeOffset ServerTime, int WithinDays);
public record LicenseOperationRequest(Guid RequestId, Guid[] LicenseIds, string Action, long? AdditionalSeconds = null, string? Note = null, string[]? Tags = null, [property: System.Text.Json.Serialization.JsonRequired] string Reason = "");
public record LicenseOperationItem(Guid LicenseId, bool Success, string? ErrorCode, LicenseDto? License);
public record OperationResult(Guid RequestId, string Action, DateTimeOffset CompletedAt, int Succeeded, int Failed, IReadOnlyList<LicenseOperationItem> Items);
public record SessionDto(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset AbsoluteExpiresAt, DateTimeOffset LeaseExpiresAt, DateTimeOffset LastSeenAt, DateTimeOffset? RevokedAt);
public record BindingDto(Guid Id, string? DeviceLabel, DateTimeOffset BoundAt, DateTimeOffset? ReleasedAt, IReadOnlyList<SessionDto> Sessions);
public record LicenseDetail(LicenseDto License, IReadOnlyList<BindingDto> Bindings);
public record AuditDto(Guid Id, string ActorId, string Action, Guid TargetId, string Changes, DateTimeOffset CreatedAt, string TraceId,
    string Category = "operation", string Result = "succeeded", string? ActorName = null, string? TargetType = null, string? TargetName = null,
    Guid? ApplicationId = null, string? ApplicationName = null, string? SourceIp = null, string? Method = null, string? ErrorCategory = null);
public record DashboardDto(int TotalLicenses, int ActivatedLicenses, int ValidLicenses, int OnlineSessions);
public record ActivateLicense(string AppCode, string LicenseKey, string DeviceId, string? DeviceLabel = null, string? ClientVersion = null);
public record ActivationDto(string SessionToken, DateTimeOffset ServerTime, DateTimeOffset ExpiresAt, DateTimeOffset LeaseExpiresAt, DateTimeOffset AbsoluteExpiresAt, int HeartbeatIntervalSeconds = 5)
{
    public Guid LicenseId { get; init; }
    public bool IsTrial { get; init; }
    public string[] Features { get; init; } = [];
    public ClientVersionInfo? Version { get; init; }
}
public record HeartbeatDto(DateTimeOffset ServerTime, DateTimeOffset ExpiresAt, DateTimeOffset LeaseExpiresAt, DateTimeOffset AbsoluteExpiresAt, int HeartbeatIntervalSeconds = 5)
{
    public Guid LicenseId { get; init; }
    public bool IsTrial { get; init; }
    public string[] Features { get; init; } = [];
    public ClientVersionInfo? Version { get; init; }
}
public record LoginRequest(string UserName, string Password);
public record UserDto(string Id, string UserName, IReadOnlyList<string> Roles, bool MustChangePassword = false, bool TwoFactorEnabled = false);
public record CsrfDto(string Token);
public record SearchLicense(string LicenseKey);
public record Actor(string Id, string TraceId);

public interface ISecretService
{
    GeneratedKey GenerateLicense();
    (string Digest, string Version) DigestLicense(string key);
    string DigestDevice(Guid applicationId, string deviceId);
    (string Token, string Digest) GenerateSession();
    string DigestToken(string token);
}
public record GeneratedKey(string Plaintext, string Digest, string Version, string Hint);
