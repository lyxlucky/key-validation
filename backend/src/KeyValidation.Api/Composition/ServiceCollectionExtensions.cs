using KeyValidation.Api.DeveloperIntegration;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KeyValidation.Api;
public static class ServiceCollectionExtensions
{
    public static WebApplicationBuilder AddKeyValidation(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 65536);
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole();
        var services = builder.Services;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DeveloperDocumentationCatalog>();
        services.AddDeveloperMcp(builder.Configuration);
        services.AddApplication();
        services.AddInfrastructure(builder.Configuration);
        services.AddScoped<AdminAccountService>();
        services.AddScoped<AccountSecurityService>();
        services.AddScoped<AdminAccessService>();
        services.AddSingleton<ActivationAdmission>();
        services.AddScoped<ActivationAdmissionActionFilter>();
        services.AddControllers(options =>
        {
            options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
            foreach (var formatter in options.InputFormatters.OfType<Microsoft.AspNetCore.Mvc.Formatters.SystemTextJsonInputFormatter>())
                formatter.SupportedMediaTypes.Remove("text/json");
        }).ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = _ => new ApiErrorResult();
        });
        services.Replace(ServiceDescriptor.Singleton<IClientErrorFactory, ApiClientErrorFactory>());
        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer<MvcContractOperationTransformer>();
            options.AddDocumentTransformer<SecurityDocumentTransformer>();
        });
        services.AddProblemDetails();
        services.AddAccountSecurity(builder.Configuration, builder.Environment);
        services.AddHttpPolicies(builder.Configuration, builder.Environment);
        return builder;
    }
}
