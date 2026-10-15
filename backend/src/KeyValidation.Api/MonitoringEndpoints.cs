using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class MonitoringEndpoints
{
    public static void MapMonitoringApi(this WebApplication app)
    {
        var monitoring = app.MapGroup("/api/admin/monitoring").RequireAuthorization("Admin").WithTags("Service monitoring");
        monitoring.MapGet("", (MonitoringService service, CancellationToken cancellationToken) => service.GetAsync(cancellationToken));
        monitoring.MapPost("/sample", (MonitoringService service, CancellationToken cancellationToken) => service.SampleAsync(cancellationToken));
        monitoring.MapGet("/metrics", async (MonitoringService service, CancellationToken cancellationToken) => TypedResults.Text(await service.PrometheusAsync(cancellationToken), "text/plain; version=0.0.4; charset=utf-8"));
    }
}
