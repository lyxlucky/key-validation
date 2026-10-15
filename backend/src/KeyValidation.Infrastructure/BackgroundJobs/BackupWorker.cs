using KeyValidation.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeyValidation.Infrastructure;
// A native backup may take minutes. Its separate loop keeps one-minute risk
// scans and monitoring samples responsive while pg_dump is running.
public sealed class BackupWorker(IServiceScopeFactory scopes, IConfiguration configuration, BackupSettings settings, MonitoringState state, TimeProvider clock, ILogger<BackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Operations:WorkersEnabled", true))
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), clock, stoppingToken);
                if (!settings.Enabled)
                {
                    state.WorkerRecovered("backup");
                    continue;
                }

                await using var scope = scopes.CreateAsyncScope();
                var result = await scope.ServiceProvider.GetRequiredService<BackupService>().RunScheduledAsync(stoppingToken);
                if (result?.Status == "failed")
                    throw new BackupFailure(result.ErrorCode ?? "BACKUP_OPERATION_FAILED");
                if (result is not null)
                    state.WorkerRecovered("backup");
            }
            catch (OperationCanceledException)when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                var code = error is BackupFailure backup ? backup.Code : error.GetType().Name;
                state.WorkerFailed("backup", code, clock.GetUtcNow());
                logger.LogWarning("Backup schedule failed: {ErrorCode}", code);
            }
        }
    }
}
