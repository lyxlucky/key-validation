using KeyValidation.Application;

namespace KeyValidation.Infrastructure;

internal sealed record RequestWindow(DateTimeOffset Start, DateTimeOffset End, long Count, long Errors, double? P95, MonitoringRouteDto[] Routes);

public sealed class RequestMetrics(TimeProvider clock) : IValidationRequestMetrics
{
    public static readonly string[] Routes = ["/api/client/licenses/activate", "/api/client/trials/activate", "/api/client/sessions/heartbeat", "/api/client/sessions/logout", "/api/client/device-bindings/release"];
    private readonly object gate = new();
    private readonly Dictionary<string, Histogram> window = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Requests, long Errors)> totals = new(StringComparer.Ordinal);
    private DateTimeOffset start = clock.GetUtcNow();
    public void Record(string? route, int statusCode, double elapsedMilliseconds)
    {
        if (route is null || !Routes.Contains(route, StringComparer.Ordinal) || !double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0) return;
        lock (gate)
        {
            if (!window.TryGetValue(route, out var samples)) window[route] = samples = new();
            samples.Add(elapsedMilliseconds, statusCode >= 500);
            var total = totals.GetValueOrDefault(route); totals[route] = (total.Requests + 1, total.Errors + (statusCode >= 500 ? 1 : 0));
        }
    }
    internal RequestWindow Collect(DateTimeOffset end)
    {
        lock (gate)
        {
            var aggregate = new Histogram();
            foreach (var histogram in window.Values) aggregate.Merge(histogram);
            var routes = window.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => new MonitoringRouteDto(x.Key, x.Value.Count, x.Value.Errors,
                x.Value.Count == 0 ? null : (double)x.Value.Errors / x.Value.Count, x.Value.P95())).ToArray();
            var result = new RequestWindow(start, end, aggregate.Count, aggregate.Errors, aggregate.P95(), routes);
            start = end; window.Clear(); return result;
        }
    }
    public IReadOnlyDictionary<string, (long Requests, long Errors)> Totals() { lock (gate) return new Dictionary<string, (long Requests, long Errors)>(totals); }

    // Fixed one-millisecond buckets bound memory regardless of traffic. Values
    // above 60 seconds share an overflow bucket whose upper value is observed.
    private sealed class Histogram
    {
        private readonly long[] buckets = new long[60002];
        private double largest;
        public long Count { get; private set; }
        public long Errors { get; private set; }
        public void Add(double value, bool error) { buckets[(int)Math.Min(60001, Math.Floor(value))]++; Count++; if (error) Errors++; largest = Math.Max(largest, value); }
        public void Merge(Histogram other) { for (var i = 0; i < buckets.Length; i++) buckets[i] += other.buckets[i]; Count += other.Count; Errors += other.Errors; largest = Math.Max(largest, other.largest); }
        public double? P95()
        {
            if (Count == 0) return null;
            var rank = (long)Math.Ceiling(Count * 0.95); long observed = 0;
            for (var i = 0; i < buckets.Length; i++) { observed += buckets[i]; if (observed >= rank) return i == 60001 ? largest : i; }
            return null;
        }
    }
}
