namespace KeyValidation.Api;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class MonitoringEndpoints
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapMonitoringApi(this WebApplication app) => app.MapControllers();
}
