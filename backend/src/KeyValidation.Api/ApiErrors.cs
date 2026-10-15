using System.Diagnostics.Metrics;
using KeyValidation.Application;

namespace KeyValidation.Api;

internal static class ApiErrors
{
    public static Task Write(HttpContext context, int status, string code, Guid? batchId = null, ClientVersionInfo? version = null)
    {
        if (context.Response.HasStarted) return Task.CompletedTask;
        if (context.Request.Path == "/api/client/sessions/heartbeat") ApiMetrics.HeartbeatRejections.Add(1, new KeyValuePair<string, object?>("reason", code));
        if (context.Request.Path == "/api/client/licenses/activate") ApiMetrics.Activations.Add(1, new KeyValuePair<string, object?>("outcome", code));
        var extensions = new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier };
        if (batchId is not null) extensions["batchId"] = batchId;
        if (version is not null) extensions["version"] = version;
        return Results.Problem(type: "about:blank", title: code, statusCode: status, extensions: extensions).ExecuteAsync(context);
    }
}
internal static class ApiMetrics
{
    private static readonly Meter Meter = new("KeyValidation.Api", "1.0");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("api.request.duration", "ms");
    internal static readonly Counter<long> Activations = Meter.CreateCounter<long>("licensing.activations");
    internal static readonly Counter<long> HeartbeatRejections = Meter.CreateCounter<long>("licensing.heartbeat.rejections");
}
