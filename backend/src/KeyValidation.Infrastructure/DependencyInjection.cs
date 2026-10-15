using KeyValidation.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace KeyValidation.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("Database") ?? throw new InvalidOperationException("Database configuration required.")));
        services.AddSingleton<ISecretService, SecretService>();
        services.AddSingleton(services => new ClientVersionOptions((services.GetRequiredService<IHostEnvironment>().IsDevelopment() || services.GetRequiredService<IHostEnvironment>().IsEnvironment("Testing")) && services.GetRequiredService<IConfiguration>().GetValue("ClientVersions:AllowLoopbackHttp", false)));
        services.AddSingleton<IOfflineLicenseSigner, OfflineLicenseKeyring>();
        services.AddScoped<ICommerceData>(services => services.GetRequiredService<AppDbContext>());
        services.AddScoped<ChannelService>();
        services.AddScoped<ApiCredentialService>();
        services.AddScoped<CommerceIdentity>();
        services.AddScoped<CommerceAuthentication>();
        services.AddSingleton<CredentialRateLimiter>();
        services.AddScoped<IOrderDeliveryProtection, OrderDeliveryProtection>();
        services.AddScoped<AuditLogService>();
        services.AddScoped<IDurableEventPublisher, DurableEventPublisher>();
        services.AddScoped<RiskService>();
        services.AddScoped<RecipientAccessService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<OperationsScanner>();
        services.AddSingleton<WebhookTargetPolicy>();
        services.AddSingleton<WebhookTransport>();
        services.AddScoped<WebhookService>();
        services.AddScoped<IWebhookDeliveryProcessor, WebhookDeliveryProcessor>();
        services.AddSingleton<BackupSettings>();
        services.AddSingleton<BackupArchive>();
        services.AddSingleton<BackupNativeProcess>();
        services.AddSingleton<BackupDatabaseRestorer>();
        services.AddSingleton<BackupRecoveryService>();
        services.AddScoped<BackupService>();
        services.AddSingleton<RequestMetrics>();
        services.AddSingleton<IValidationRequestMetrics>(services => services.GetRequiredService<RequestMetrics>());
        services.AddSingleton<MonitoringState>();
        services.AddSingleton<IDatabaseAvailabilityProbe, DatabaseAvailabilityProbe>();
        services.AddScoped<MonitoringService>();
        services.AddScoped<ReliabilityRetention>();
        services.AddHostedService<OperationsWorker>();
        services.AddHostedService<BackupWorker>();
        services.AddScoped<ILicensingData>(services => services.GetRequiredService<AppDbContext>());
        services.AddHostedService<RetentionWorker>();
        return services;
    }
}
