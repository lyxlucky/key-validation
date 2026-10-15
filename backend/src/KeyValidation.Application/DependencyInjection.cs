using Microsoft.Extensions.DependencyInjection;

namespace KeyValidation.Application;
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<LicensingService>();
        services.AddScoped<PlanCatalogService>();
        services.AddScoped<PlanAvailability>();
        services.AddScoped<PlanCatalogQueries>();
        services.AddScoped<ClientVersionPolicyService>();
        services.AddScoped<OfflineLicenseService>();
        services.AddScoped<ApplicationAccessScope>();
        services.AddScoped<CommerceWorkflow>();
        services.AddScoped<CustomerService>();
        services.AddScoped<OrderService>();
        services.AddScoped<OrderFulfillment>();
        services.AddScoped<PortalRequestService>();
        services.AddScoped<ClientRequestFacts>();
        return services;
    }
}
