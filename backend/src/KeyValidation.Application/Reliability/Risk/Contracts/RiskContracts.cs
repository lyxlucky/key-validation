namespace KeyValidation.Application;
public record RiskPolicyDto(Guid ApplicationId, bool Enabled, int ActivationThreshold, int ActivationWindowMinutes, int RebindingThreshold, int RebindingWindowMinutes, int RegionWindowHours);
public record UpdateRiskPolicy(bool Enabled, int ActivationThreshold = 10, int ActivationWindowMinutes = 10, int RebindingThreshold = 3, int RebindingWindowMinutes = 60, int RegionWindowHours = 24);
public record RiskEventDto(Guid Id, Guid ApplicationId, Guid LicenseId, string Rule, string Summary, string EvidenceJson, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, string Status, DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string? ReviewedBy, string? ReviewReason);
public record ReviewRiskEvent(string Status, string Reason);
