using KeyValidation.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace KeyValidation.Infrastructure;
public sealed class OperationsWorker(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider clock, MonitoringState state, ILogger<OperationsWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("Operations:WorkersEnabled", true))
            return Task.CompletedTask;
        // Each loop owns its DI/database scope. Slow external delivery cannot
        // delay sampling or scanning; database locks coordinate other instances.
        return Task.WhenAll(Loop("monitoring", TimeSpan.FromSeconds(60), async services =>
        {
            await services.GetRequiredService<MonitoringService>().SampleAsync(stoppingToken);
        }, stoppingToken), Loop("scan", TimeSpan.FromSeconds(60), async services =>
        {
            await services.GetRequiredService<OperationsScanner>().ScanAsync(stoppingToken);
        }, stoppingToken), Loop("webhook", TimeSpan.FromSeconds(5), async services =>
        {
            await services.GetRequiredService<IWebhookDeliveryProcessor>().DeliverDueAsync(stoppingToken);
        }, stoppingToken), Loop("retention", TimeSpan.FromHours(6), services => services.GetRequiredService<ReliabilityRetention>().CleanupAsync(stoppingToken), stoppingToken));
    }

    private async Task Loop(string name, TimeSpan interval, Func<IServiceProvider, Task> operation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, clock, cancellationToken);
                await using var scope = scopes.CreateAsyncScope();
                await operation(scope.ServiceProvider);
                state.WorkerRecovered(name);
            }
            catch (OperationCanceledException)when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                var code = error.GetType().Name;
                state.WorkerFailed(name, code, clock.GetUtcNow());
                logger.LogWarning("Operations worker {Worker} failed: {ErrorCode}", name, code);
            }
        }
    }
}
