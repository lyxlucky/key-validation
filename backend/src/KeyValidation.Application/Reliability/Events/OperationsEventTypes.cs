namespace KeyValidation.Application;
public static class OperationsEventTypes
{
    public const string LicenseExpiring = "license.expiring";
    public const string RiskDetected = "risk.detected";
    public const string BatchCompleted = "batch.completed";
    public const string ServiceDegraded = "service.degraded";
    public const string ServiceRecovered = "service.recovered";
    public const string OrderFulfilled = "order.fulfilled";
    public const string LicenseRenewed = "license.renewed";
    public const string BindingReleased = "binding.released";
    public static readonly string[] All = [LicenseExpiring, RiskDetected, BatchCompleted, ServiceDegraded, ServiceRecovered, OrderFulfilled, LicenseRenewed, BindingReleased];
}
