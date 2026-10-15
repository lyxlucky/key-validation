using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeyValidation.Infrastructure;

public sealed class RetentionWorker(IServiceScopeFactory scopes, TimeProvider clock, IConfiguration config, ILogger<RetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var now = clock.GetUtcNow();
                var audits = now.AddDays(-Math.Max(1, config.GetValue("Retention:AuditDays", 180)));
                var events = now.AddDays(-Math.Max(1, config.GetValue("Retention:EventDays", 30)));
                var sessions = now.AddDays(-Math.Max(1, config.GetValue("Retention:SessionDays", 30)));
                await db.AuditLogs.Where(x => x.CreatedAt < audits).ExecuteDeleteAsync(stoppingToken);
                await db.AuthorizationEvents.Where(x => x.CreatedAt < events).ExecuteDeleteAsync(stoppingToken);
                await db.Sessions.Where(x => x.AbsoluteExpiresAt < sessions || x.RevokedAt < sessions).ExecuteDeleteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception error) { logger.LogWarning("Retention cleanup deferred: {ErrorType}", error.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromHours(6), clock, stoppingToken); } catch (OperationCanceledException) { return; }
        }
    }
}
