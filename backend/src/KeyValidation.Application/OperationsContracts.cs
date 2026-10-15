using System.Text.Json.Serialization;

namespace KeyValidation.Application;

public record AccountAccessDto(string[] Permissions, bool AllApplications, Guid[] ApplicationIds, Guid? ChannelId);
public record UpdateAccountAccess(
    string[]? Permissions,
    [property: JsonRequired] bool AllApplications,
    [property: JsonRequired] Guid[] ApplicationIds,
    Guid? ChannelId = null);

public record LicenseQuery(
    int Page = 1, int PageSize = 20, Guid? ApplicationId = null, Guid? BatchId = null,
    string? Status = null, DateTimeOffset? From = null, DateTimeOffset? To = null,
    DateTimeOffset? ActivatedFrom = null, DateTimeOffset? ActivatedTo = null,
    DateTimeOffset? ExpiresFrom = null, DateTimeOffset? ExpiresTo = null,
    string? Note = null, string? Tag = null, string? DeviceId = null, Guid? ChannelId = null);

public record OperationsCounts(int Issued, int Activated, int Expired, int OnlineLicenses, int OnlineSessions);
public record OperationsDay(DateOnly Date, int Issued, int Activated, int Expired);
public record OperationsBreakdown(Guid ApplicationId, string ApplicationName, Guid? PlanId, string? PlanName,
    int Issued, int Activated, int Expired, int OnlineLicenses, int OnlineSessions);
public record OperationsReport(DateTimeOffset From, DateTimeOffset To, DateTimeOffset GeneratedAt, string TimeZone,
    OperationsCounts Totals, IReadOnlyList<OperationsDay> Daily, IReadOnlyList<OperationsBreakdown> Breakdown);
public record ReportApplicationDto(Guid Id, string Name);
public record ReportPlanDto(Guid Id, Guid ApplicationId, string Name, bool IsEnabled);
public record ReportsCatalogDto(IReadOnlyList<ReportApplicationDto> Applications, IReadOnlyList<ReportPlanDto> Plans);
