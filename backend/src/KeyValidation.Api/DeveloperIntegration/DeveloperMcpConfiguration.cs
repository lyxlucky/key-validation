using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;

namespace KeyValidation.Api.DeveloperIntegration;
public static class DeveloperMcpConfiguration
{
    public static bool IsMcpRequest(HttpContext context) => context.Request.Path.Value?.TrimEnd('/').Equals("/mcp", StringComparison.OrdinalIgnoreCase) == true;
    public static bool IsPublicDeveloperRequest(HttpContext context)
    {
        var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
        const string docs = "/api/integrations/developer-docs";
        return IsMcpRequest(context) || ((context.Request.Method is "GET" or "HEAD") && (path.Equals(docs, StringComparison.OrdinalIgnoreCase) || (path.StartsWith(docs + "/", StringComparison.OrdinalIgnoreCase) && !path[(docs.Length + 1)..].Contains('/'))));
    }

    public static IServiceCollection AddDeveloperMcp(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<DeveloperExampleCatalog>();
        services.AddSingleton<DeveloperTroubleshootingCatalog>();
        var resources = new DeveloperMcpResources(new DeveloperDocumentationCatalog());
        services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<DeveloperMcpTools>().WithRequestFilters(filters => filters.AddCallToolFilter(next => (request, cancellationToken) =>
        {
            string[] allowed = request.Params!.Name switch
            {
                "search_integration_docs" => ["query", "limit"],
                "get_integration_doc" => ["docId"],
                "get_api_reference" => ["operationId"],
                "get_code_example" => ["language", "scenario"],
                "get_troubleshooting_guide" => ["errorCode", "operationId"],
                _ => []
            };
            if (request.Params.Arguments?.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)) == true)
                return ValueTask.FromResult(new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "Unsupported tool argument. Only documented public catalog inputs are accepted." }] });
            return next(request, cancellationToken);
        })).WithListResourcesHandler((_, _) => ValueTask.FromResult(resources.List())).WithReadResourceHandler((request, _) => ValueTask.FromResult(resources.Read(request.Params!.Uri)));
        services.AddRateLimiter(o => o.AddPolicy("developer-docs", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = configuration.GetValue("RateLimiting:DeveloperDocsPerMinute", 60), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        return services;
    }

    public static void MapDeveloperMcp(this IEndpointRouteBuilder app) => app.MapMcp("/mcp").AllowAnonymous().RequireRateLimiting("developer-docs");
    public static async Task GuardRequest(HttpContext context, RequestDelegate next)
    {
        if (!IsMcpRequest(context))
        {
            await next(context);
            return;
        }

        var config = context.RequestServices.GetRequiredService<IConfiguration>();
        var hosts = config.GetSection("DeveloperMcp:AllowedHosts").Get<string[]>() ?? ["localhost", "127.0.0.1", "[::1]"];
        var origins = config.GetSection("DeveloperMcp:AllowedOrigins").Get<string[]>() ?? [];
        if (!hosts.Contains(context.Request.Host.Host, StringComparer.OrdinalIgnoreCase) || (context.Request.Headers.TryGetValue("Origin", out var origin) && (origin.Count != 1 || !origins.Contains(origin[0], StringComparer.Ordinal))))
        {
            context.Response.StatusCode = 403;
            return;
        }

        var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (feature is { IsReadOnly: false })
            feature.MaxRequestBodySize = 65536;
        var original = context.Request.Body;
        var limited = new LimitedBody(original);
        context.Request.Body = limited;
        context.Response.OnStarting(() =>
        {
            if (limited.Exceeded)
                context.Response.StatusCode = 413;
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
        }
        finally
        {
            context.Request.Body = original;
        }
    }

    private sealed class LimitedBody(Stream inner) : Stream
    {
        private long read;
        public bool Exceeded { get; private set; }

        private int Count(int value)
        {
            read += value;
            if (read > 65536)
            {
                Exceeded = true;
                throw new BadHttpRequestException("Request exceeds 64 KiB.", 413);
            }

            return value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer, cancellationToken));
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
