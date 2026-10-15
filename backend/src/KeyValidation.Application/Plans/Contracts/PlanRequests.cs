namespace KeyValidation.Application;
public record PlanDto(Guid Id, Guid ApplicationId, string Name, long DurationSeconds, int MaxDevices, bool IsEnabled, DateTimeOffset CreatedAt, int MaxConcurrentSessions, int? ActivationValidityDays, string[] Features);
public record CreatePlan(string Name, long DurationSeconds, int MaxDevices = 1, int? MaxConcurrentSessions = null, int? ActivationValidityDays = null, string[]? Features = null);
public record UpdatePlan(string Name, long DurationSeconds, int MaxDevices, [property: System.Text.Json.Serialization.JsonRequired] bool IsEnabled, int? MaxConcurrentSessions = null, int? ActivationValidityDays = null, string[]? Features = null);
