namespace KeyValidation.Application;
public record MonitoringRouteDto(string Route, long RequestCount, long ServerErrorCount, double? ErrorRate, double? P95LatencyMs);
public record MonitoringSampleDto(Guid Id, DateTimeOffset SampledAt, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, bool DatabaseAvailable, long RequestCount, long ServerErrorCount, double? ErrorRate, double? P95LatencyMs, MonitoringRouteDto[] Routes);
public record MonitoringIncidentDto(Guid Id, string Kind, string Summary, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt);
public record WorkerErrorDto(string Worker, string ErrorCode, DateTimeOffset OccurredAt);
public record MonitoringDto(DateTimeOffset GeneratedAt, bool DatabaseAvailable, MonitoringSampleDto? Latest, MonitoringSampleDto[] Samples, MonitoringIncidentDto[] Incidents, WorkerErrorDto[] WorkerErrors);
