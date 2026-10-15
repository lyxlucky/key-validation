using System.Text.Json.Serialization;

namespace KeyValidation.Application;
public record OperationsCounts(int Issued, int Activated, int Expired, int OnlineLicenses, int OnlineSessions);
public record OperationsDay(DateOnly Date, int Issued, int Activated, int Expired);
public record OperationsBreakdown(Guid ApplicationId, string ApplicationName, Guid? PlanId, string? PlanName, int Issued, int Activated, int Expired, int OnlineLicenses, int OnlineSessions);
public record OperationsReport(DateTimeOffset From, DateTimeOffset To, DateTimeOffset GeneratedAt, string TimeZone, OperationsCounts Totals, IReadOnlyList<OperationsDay> Daily, IReadOnlyList<OperationsBreakdown> Breakdown);
public record ReportApplicationDto(Guid Id, string Name);
public record ReportPlanDto(Guid Id, Guid ApplicationId, string Name, bool IsEnabled);
public record ReportsCatalogDto(IReadOnlyList<ReportApplicationDto> Applications, IReadOnlyList<ReportPlanDto> Plans);
