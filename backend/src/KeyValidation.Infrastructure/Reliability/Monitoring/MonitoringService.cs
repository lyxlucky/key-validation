using System.Globalization;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace KeyValidation.Infrastructure;
public sealed class MonitoringService(AppDbContext db, ApplicationAccessScope access, RequestMetrics metrics, MonitoringState state, IDatabaseAvailabilityProbe probe, IDurableEventPublisher publisher, TimeProvider clock)
{
    public async Task<MonitoringDto> GetAsync(CancellationToken cancellationToken = default)
    {
        access.Require(ApplicationPermissions.Administrator);
        var now = clock.GetUtcNow();
        var available = await probe.IsAvailableAsync(cancellationToken);
        if (available)
        {
            try
            {
                var samples = await db.MonitoringSamples.AsNoTracking().OrderByDescending(x => x.SampledAt).ThenByDescending(x => x.Id).Take(120).ToArrayAsync(cancellationToken);
                var incidents = await db.MonitoringIncidents.AsNoTracking().OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Take(100).ToArrayAsync(cancellationToken);
                return new(now, true, samples.FirstOrDefault()is { } latest ? Dto(latest) : null, samples.Select(Dto).ToArray(), incidents.Select(Dto).ToArray(), state.WorkerErrors());
            }
            catch (Exception error)when (DatabaseError(error))
            {
                available = false;
            }
        }

        var memory = state.Recent();
        var outage = state.Outage();
        return new(now, available, memory.FirstOrDefault()is { } recent ? Dto(recent) : null, memory.Select(Dto).ToArray(), outage is null ? [] : [Dto(outage)], state.WorkerErrors());
    }

    public async Task<MonitoringSampleDto> SampleAsync(CancellationToken cancellationToken = default)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        await state.Sampling.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var window = metrics.Collect(now);
            var available = await probe.IsAvailableAsync(cancellationToken);
            var sample = new MonitoringSample
            {
                InstanceId = state.InstanceId,
                SampledAt = now,
                WindowStart = window.Start,
                WindowEnd = window.End,
                DatabaseAvailable = available,
                RequestCount = window.Count,
                ServerErrorCount = window.Errors,
                ErrorRate = window.Count == 0 ? null : (double)window.Errors / window.Count,
                P95LatencyMs = window.P95,
                RoutesJson = JsonSerializer.Serialize(window.Routes)
            };
            state.Add(sample);
            if (!available)
            {
                state.DatabaseFailed(now);
                return Dto(sample);
            }

            var pending = state.Pending();
            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836050, 4)", cancellationToken);
                var ids = pending.Select(x => x.Id).ToArray();
                var saved = (await db.MonitoringSamples.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync(cancellationToken)).ToHashSet();
                db.MonitoringSamples.AddRange(pending.Where(x => !saved.Contains(x.Id)));
                await db.SaveChangesAsync(cancellationToken);
                await ReconcileDatabaseOutage(now, cancellationToken);
                var since = now.AddMinutes(-6);
                var recent = await db.MonitoringSamples.AsNoTracking().Where(x => x.SampledAt >= since).ToArrayAsync(cancellationToken);
                var sequences = recent.GroupBy(x => x.InstanceId).Select(group => group.OrderByDescending(x => x.SampledAt).ThenByDescending(x => x.Id).Take(3).ToArray()).ToArray();
                await ReconcileThreshold("error_rate", "验证接口 5xx 错误率持续超过阈值", sequences.Any(ThreeErrors), now, cancellationToken);
                await ReconcileThreshold("high_latency", "验证接口 p95 延迟持续超过阈值", sequences.Any(ThreeSlow), now, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                state.Persisted(ids);
            }
            catch (Exception error)when (DatabaseError(error))
            {
                sample.DatabaseAvailable = false;
                state.DatabaseFailed(now);
            }

            return Dto(sample);
        }
        finally
        {
            state.Sampling.Release();
        }
    }

    private static bool ThreeErrors(MonitoringSample[] samples) => samples.Length == 3 && samples.All(x => x.DatabaseAvailable && x.RequestCount >= 20 && x.ErrorRate >= 0.05);
    private static bool ThreeSlow(MonitoringSample[] samples) => samples.Length == 3 && samples.All(x => x.DatabaseAvailable && x.RequestCount >= 20 && x.P95LatencyMs >= 500);
    private async Task ReconcileDatabaseOutage(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var memory = state.Outage();
        if (memory is not null)
        {
            var record = await db.MonitoringIncidents.Where(x => x.Kind == "database_unavailable" && x.StartedAt <= now && (x.ResolvedAt == null || x.ResolvedAt >= memory.StartedAt)).OrderBy(x => x.StartedAt).FirstOrDefaultAsync(cancellationToken);
            if (record is null)
            {
                record = new()
                {
                    Id = memory.Id,
                    Kind = memory.Kind,
                    Summary = memory.Summary,
                    StartedAt = memory.StartedAt
                };
                db.MonitoringIncidents.Add(record);
                await PublishIncident(record, false, cancellationToken);
            }

            if (record.ResolvedAt is null)
            {
                record.ResolvedAt = now;
                await PublishIncident(record, true, cancellationToken);
            }
        }

        var open = await db.MonitoringIncidents.Where(x => x.Kind == "database_unavailable" && x.ResolvedAt == null).ToArrayAsync(cancellationToken);
        foreach (var record in open)
        {
            record.ResolvedAt = now;
            await PublishIncident(record, true, cancellationToken);
        }
    }

    private async Task ReconcileThreshold(string kind, string summary, bool active, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var incident = await db.MonitoringIncidents.SingleOrDefaultAsync(x => x.Kind == kind && x.ResolvedAt == null, cancellationToken);
        if (active && incident is null)
        {
            incident = new()
            {
                Kind = kind,
                Summary = summary,
                StartedAt = now
            };
            db.MonitoringIncidents.Add(incident);
            await PublishIncident(incident, false, cancellationToken);
        }
        else if (!active && incident is not null)
        {
            incident.ResolvedAt = now;
            await PublishIncident(incident, true, cancellationToken);
        }
    }

    private async Task PublishIncident(MonitoringIncident incident, bool recovered, CancellationToken cancellationToken)
    {
        var type = recovered ? OperationsEventTypes.ServiceRecovered : OperationsEventTypes.ServiceDegraded;
        var applications = await db.NotificationPolicies.Where(x => x.Enabled && x.EventTypes.Contains(type)).Select(x => x.ApplicationId).Union(db.WebhookSubscriptions.Where(x => x.IsEnabled && x.EventTypes.Contains(type)).Select(x => x.ApplicationId)).OrderBy(x => x).ToArrayAsync(cancellationToken);
        foreach (var applicationId in applications)
            await publisher.PublishAsync(applicationId, incident.Id, type, JsonSerializer.Serialize(new { kind = incident.Kind, startedAt = incident.StartedAt, resolvedAt = incident.ResolvedAt }), "incident:" + incident.Id + ":" + type, cancellationToken);
    }

    public async Task<string> PrometheusAsync(CancellationToken cancellationToken = default)
    {
        access.Require(ApplicationPermissions.Administrator);
        var available = await probe.IsAvailableAsync(cancellationToken);
        var text = new StringBuilder("# TYPE keygate_database_available gauge\nkeygate_database_available ").Append(available ? '1' : '0').Append('\n');
        text.Append("# TYPE keygate_validation_requests_total counter\n# TYPE keygate_validation_server_errors_total counter\n");
        foreach (var route in metrics.Totals().OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            text.Append("keygate_validation_requests_total{route=\"").Append(route.Key).Append("\"} ").Append(route.Value.Requests.ToString(CultureInfo.InvariantCulture)).Append('\n');
            text.Append("keygate_validation_server_errors_total{route=\"").Append(route.Key).Append("\"} ").Append(route.Value.Errors.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        foreach (var sample in state.Recent().Take(1))
            foreach (var route in Dto(sample).Routes.Where(x => x.P95LatencyMs is not null))
                text.Append("keygate_validation_duration_p95_milliseconds{route=\"").Append(route.Route).Append("\"} ").Append(route.P95LatencyMs!.Value.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var error in state.WorkerErrors())
            text.Append("keygate_worker_failure{worker=\"").Append(error.Worker).Append("\"} 1\n");
        return text.ToString();
    }

    private static bool DatabaseError(Exception error) => error is NpgsqlException or TimeoutException || error is DbUpdateException { InnerException: NpgsqlException };
    internal static MonitoringSampleDto Dto(MonitoringSample x) => new(x.Id, x.SampledAt, x.WindowStart, x.WindowEnd, x.DatabaseAvailable, x.RequestCount, x.ServerErrorCount, x.ErrorRate, x.P95LatencyMs, JsonSerializer.Deserialize<MonitoringRouteDto[]>(x.RoutesJson) ?? []);
    private static MonitoringIncidentDto Dto(MonitoringIncident x) => new(x.Id, x.Kind, x.Summary, x.StartedAt, x.ResolvedAt);
}
