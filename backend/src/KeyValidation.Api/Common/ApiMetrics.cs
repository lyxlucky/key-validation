using System.Diagnostics.Metrics;
using KeyValidation.Application;

namespace KeyValidation.Api;
internal static class ApiMetrics
{
    private static readonly Meter Meter = new("KeyValidation.Api", "1.0");
    internal static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("api.request.duration", "ms");
    internal static readonly Counter<long> Activations = Meter.CreateCounter<long>("licensing.activations");
    internal static readonly Counter<long> HeartbeatRejections = Meter.CreateCounter<long>("licensing.heartbeat.rejections");
}
