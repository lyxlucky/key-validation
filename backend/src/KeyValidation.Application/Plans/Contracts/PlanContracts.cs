using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record GlobalPlanDto(Guid Id, string Name, long DurationSeconds, int MaxDevices, bool IsEnabled, DateTimeOffset CreatedAt, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features, int ReferenceCount);
public record ApplicationPlanDto(Guid ApplicationId, Guid PlanId, string Name, long DurationSeconds, int MaxDevices, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features, bool PlanEnabled, bool IsEnabled, bool CanIssue, DateTimeOffset CreatedAt);
public record PlanReferenceDto(Guid ApplicationId, string ApplicationName, bool IsEnabled, DateTimeOffset CreatedAt);
public record SetPlanReference([property: JsonRequired] bool IsEnabled);
