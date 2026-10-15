using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace KeyValidation.Infrastructure;

public sealed class DatabaseAvailabilityProbe(IConfiguration configuration) : IDatabaseAvailabilityProbe
{
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var cs = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Database")) { Pooling = false, Timeout = 3, CommandTimeout = 3 };
            await using var connection = new NpgsqlConnection(cs.ConnectionString); await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection); await command.ExecuteScalarAsync(cancellationToken); return true;
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException) { return false; }
    }
}

public sealed class MonitoringState
{
    internal readonly SemaphoreSlim Sampling = new(1, 1);
    internal string InstanceId { get; } = Guid.NewGuid().ToString("N");
    private readonly object gate = new();
    private readonly List<MonitoringSample> pending = [];
    private readonly Queue<MonitoringSample> recent = new();
    private readonly Dictionary<string, WorkerErrorDto> workers = new(StringComparer.Ordinal);
    private MonitoringIncident? outage;
    internal void Add(MonitoringSample sample)
    {
        lock (gate)
        {
            pending.RemoveAll(x => x.SampledAt < sample.SampledAt.AddDays(-30)); pending.Add(sample); recent.Enqueue(sample);
            while (recent.Count > 120) recent.Dequeue();
        }
    }
    internal void DatabaseFailed(DateTimeOffset now)
    {
        lock (gate) outage ??= new() { Kind = "database_unavailable", Summary = "数据库连接不可用", StartedAt = now };
    }
    internal MonitoringIncident? Outage() { lock (gate) return outage; }
    internal MonitoringSample[] Pending() { lock (gate) return pending.ToArray(); }
    internal MonitoringSample[] Recent() { lock (gate) return recent.Reverse().ToArray(); }
    internal void Persisted(IEnumerable<Guid> ids)
    {
        lock (gate) { var saved = ids.ToHashSet(); pending.RemoveAll(x => saved.Contains(x.Id)); outage = null; }
    }
    public void WorkerFailed(string name, string code, DateTimeOffset now) { lock (gate) workers[name] = new(name, code, now); }
    public void WorkerRecovered(string name) { lock (gate) workers.Remove(name); }
    public WorkerErrorDto[] WorkerErrors() { lock (gate) return workers.Values.OrderBy(x => x.Worker, StringComparer.Ordinal).ToArray(); }
}
