using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api;
public static class HttpServiceCollectionExtensions
{
    internal static IServiceCollection AddHttpPolicies(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"];
        if (environment.IsDevelopment())
            services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var proxy in configuration.GetSection("Proxy:KnownProxies").Get<string[]>() ?? [])
                o.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return new ValueTask(ApiErrors.Write(context.HttpContext, 429, "RATE_LIMITED"));
            };
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = configuration.GetValue("RateLimiting:GlobalPerMinute", 600), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = configuration.GetValue("RateLimiting:LoginPerMinute", 10), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.AddPolicy("heartbeat", context => RateLimitPartition.GetFixedWindowLimiter(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString()))), _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        return services;
    }
}
