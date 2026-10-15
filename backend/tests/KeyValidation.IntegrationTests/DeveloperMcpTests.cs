using System.Net;
using System.Text;
using System.Text.Json;
using System.Security.Claims;
using KeyValidation.Api.DeveloperIntegration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class DeveloperMcpTests
{
    private sealed class DiagnosticLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new DiagnosticLogger(categoryName);
        public void Dispose() { }
        private sealed class DiagnosticLogger(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => level >= LogLevel.Error;
            public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(level)) return;
                var template = (state as IEnumerable<KeyValuePair<string, object?>>)?.FirstOrDefault(x => x.Key == "{OriginalFormat}").Value as string;
                Console.Error.WriteLine($"MCP diagnostic: category={category}, event={eventId.Id}, level={level}");
                if (template?.StartsWith("Request failed with", StringComparison.Ordinal) == true) Console.Error.WriteLine(formatter(state, null));
                for (var current = exception; current is not null; current = current.InnerException)
                    Console.Error.WriteLine($"ExceptionType={current.GetType().FullName}\n{current.StackTrace}");
            }
        }
    }
    private sealed class PublicFactory(int limit = 1000, string environment = "Testing") : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureServices(services => services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "mcp-test-keys")))
                .UseEphemeralDataProtectionProvider());
            builder.ConfigureLogging(logging => logging.AddProvider(new DiagnosticLoggerProvider()).AddFilter<DiagnosticLoggerProvider>(null, LogLevel.Error));
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=unavailable;Password=unused;Timeout=1",
                ["Licensing:CurrentKeyVersion"] = "v1", ["Licensing:Keys:v1"] = Convert.ToBase64String(new byte[32]),
                ["Operations:WorkersEnabled"] = "false", ["RateLimiting:GlobalPerMinute"] = "1000",
                ["Logging:LogLevel:Default"] = "None",
                ["RateLimiting:DeveloperDocsPerMinute"] = limit.ToString(),
                ["DeveloperMcp:AllowedHosts:0"] = "localhost", ["DeveloperMcp:AllowedOrigins:0"] = "https://docs.example.test"
            }));
        }
    }

    private static Task<McpClient> Connect(HttpClient http, string? version = null) => McpClient.CreateAsync(
        new HttpClientTransport(new() { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp }, http),
        new() { ProtocolVersion = version, InitializationTimeout = TimeSpan.FromSeconds(5), DiscoverProbeTimeout = TimeSpan.FromSeconds(2) });

    private static string Text(CallToolResult result) => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text));

    [Theory]
    [InlineData(null)]
    [InlineData("2025-11-25")]
    public async Task AnonymousProtocolRoundTrip(string? version)
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient();
        await using var client = await Connect(http, version);
        Assert.Equal(version ?? "2026-07-28", client.NegotiatedProtocolVersion);
        var tools = await client.ListToolsAsync();
        Assert.Equal(5, tools.Count);
        Assert.All(tools, tool => Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint));
        var calls = new (string Name, Dictionary<string, object?> Args)[] {
            ("search_integration_docs", new() { ["query"] = "activation" }),
            ("get_integration_doc", new() { ["docId"] = "activation" }),
            ("get_api_reference", new() { ["operationId"] = "activate" }),
            ("get_code_example", new() { ["language"] = "python", ["scenario"] = "online-license" }),
            ("get_troubleshooting_guide", new() { ["errorCode"] = "LICENSE_INVALID" }) };
        foreach (var call in calls) { var result = await client.CallToolAsync(call.Name, call.Args); Assert.False(result.IsError == true); Assert.NotEmpty(Text(result)); }
        Assert.Contains(DeveloperDocumentationCatalog.Version, Text(await client.CallToolAsync("get_integration_doc", new Dictionary<string, object?> { ["docId"] = "activation" })));
        var resources = await client.ListResourcesAsync(); Assert.Equal(9, resources.Count);
        foreach (var resource in resources) { Assert.StartsWith("keygate://docs/", resource.Uri); var result = await client.ReadResourceAsync(resource.Uri); Assert.NotEmpty(result.Contents.OfType<TextResourceContents>().Single().Text); }
    }

    [Fact]
    public async Task InvalidToolInputsReturnErrors()
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient(); await using var client = await Connect(http);
        var cases = new (string Name, Dictionary<string, object?> Args)[] {
            ("search_integration_docs", new() { ["query"] = "" }), ("search_integration_docs", new() { ["query"] = new string('x', 201) }),
            ("search_integration_docs", new() { ["query"] = "activation", ["limit"] = 11 }),
            ("search_integration_docs", new() { ["query"] = "activation", ["limit"] = 0 }),
            ("get_integration_doc", new() { ["docId"] = "activation", ["licenseKey"] = "not-accepted" }),
            ("get_integration_doc", new() { ["docId"] = "admin" }), ("get_api_reference", new() { ["operationId"] = "admin" }),
            ("get_code_example", new() { ["language"] = "ruby", ["scenario"] = "online-license" }),
            ("get_code_example", new() { ["language"] = "python", ["scenario"] = "unknown" }),
            ("get_troubleshooting_guide", new() { ["errorCode"] = new string('x', 101) }) };
        foreach (var call in cases) Assert.True((await client.CallToolAsync(call.Name, call.Args)).IsError);
        await Assert.ThrowsAnyAsync<Exception>(async () => await client.ReadResourceAsync("keygate://docs/admin"));
    }

    [Fact]
    public async Task SearchNoMatchReturnsEmpty()
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient(); await using var client = await Connect(http);
        Assert.Equal("[]", Text(await client.CallToolAsync("search_integration_docs", new Dictionary<string, object?> { ["query"] = "no-match-unique-abc" })));
    }

    [Fact]
    public async Task UnknownCodeReturnsGenericGuide()
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient(); await using var client = await Connect(http);
        Assert.Contains("\"known\":false", Text(await client.CallToolAsync("get_troubleshooting_guide", new Dictionary<string, object?> { ["errorCode"] = "UNKNOWN" })));
    }

    [Fact]
    public async Task RateLimitHasRetryAfter()
    {
        using var factory = new PublicFactory(1); using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/integrations/developer-docs")).StatusCode);
        var response = await http.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode); Assert.NotNull(response.Headers.RetryAfter);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context) => await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 65536) + "\"}"));
    }

    [Fact]
    public async Task ChunkedOversizedBodyRejected()
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient(); using var body = new UnknownLengthContent(); body.Headers.ContentType = new("application/json");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json"); http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync("/mcp", body)).StatusCode);
    }

    [Theory]
    [InlineData(null, "localhost", 400)]
    [InlineData("https://docs.example.test", "localhost", 400)]
    [InlineData("https://evil.test", "localhost", 403)]
    [InlineData(null, "evil.test", 403)]
    public async Task OriginAndHostAreValidated(string? origin, string host, int status)
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient(); http.DefaultRequestHeaders.Host = host;
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json"); http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        if (origin is not null) http.DefaultRequestHeaders.Add("Origin", origin);
        Assert.Equal(status, (int)(await http.PostAsync("/mcp", new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode);
    }

    [Fact]
    public async Task ProductionRequiresHttps()
    {
        using var factory = new PublicFactory(environment: "Production"); using var http = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/mcp", new StringContent("{}"))).StatusCode);
        using var secure = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        await using var client = await Connect(secure); Assert.Equal(5, (await client.ListToolsAsync()).Count);
    }

    [Fact]
    public async Task CookieDoesNotExpandPublicContent()
    {
        using var factory = new PublicFactory(); using var anonymous = factory.CreateClient();
        using var authenticated = factory.CreateClient(new() { HandleCookies = false });
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(IdentityConstants.ApplicationScheme);
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "business-admin"), new Claim(ClaimTypes.Name, "admin"), new Claim(ClaimTypes.Role, "Admin")], IdentityConstants.ApplicationScheme));
        var properties = new AuthenticationProperties();
        properties.Items["absolute-issued"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        authenticated.DefaultRequestHeaders.Add("Cookie", "KeyValidation.Admin=" + options.TicketDataFormat.Protect(new(principal, properties, IdentityConstants.ApplicationScheme)));
        authenticated.DefaultRequestHeaders.Authorization = new("Bearer", "must-not-be-parsed");
        Assert.Equal(await anonymous.GetStringAsync("/api/integrations/developer-docs"), await authenticated.GetStringAsync("/api/integrations/developer-docs"));
        Assert.Equal(await anonymous.GetStringAsync("/api/integrations/developer-docs/"), await authenticated.GetStringAsync("/api/integrations/developer-docs/"));
        Assert.Equal(await anonymous.GetStringAsync("/api/integrations/developer-docs/activation/"), await authenticated.GetStringAsync("/api/integrations/developer-docs/activation/"));
        await using var first = await Connect(anonymous); await using var second = await Connect(authenticated);
        var args = new Dictionary<string, object?> { ["docId"] = "activation" };
        Assert.Equal(Text(await first.CallToolAsync("get_integration_doc", args)), Text(await second.CallToolAsync("get_integration_doc", args)));
        Assert.Equal((await first.ListResourcesAsync()).Select(x => x.Uri), (await second.ListResourcesAsync()).Select(x => x.Uri));
        using var slash = new HttpClient(factory.Server.CreateHandler()) { BaseAddress = new Uri("http://localhost") };
        slash.DefaultRequestHeaders.Add("Cookie", authenticated.DefaultRequestHeaders.GetValues("Cookie"));
        await using var slashClient = await McpClient.CreateAsync(new HttpClientTransport(new() { Endpoint = new Uri("http://localhost/mcp/"), TransportMode = HttpTransportMode.StreamableHttp }, slash));
        Assert.Equal(Text(await first.CallToolAsync("get_integration_doc", args)), Text(await slashClient.CallToolAsync("get_integration_doc", args)));
        // The same validly protected ticket on an admin route still invokes business identity checks.
        Assert.Equal(HttpStatusCode.InternalServerError, (await authenticated.GetAsync("/api/admin/applications")).StatusCode);
    }

    [Fact]
    public async Task TrailingSlashCannotBypassGuard()
    {
        using var factory = new PublicFactory(); using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Host = "evil.test";
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/mcp/", new StringContent("{}"))).StatusCode);
        http.DefaultRequestHeaders.Host = "localhost"; http.DefaultRequestHeaders.Add("Origin", "https://evil.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PostAsync("/mcp/", new StringContent("{}"))).StatusCode);
        http.DefaultRequestHeaders.Remove("Origin"); http.DefaultRequestHeaders.Accept.ParseAdd("application/json"); http.DefaultRequestHeaders.Accept.ParseAdd("text/event-stream");
        using var oversized = new UnknownLengthContent(); oversized.Headers.ContentType = new("application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync("/mcp/", oversized)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await http.PostAsync("/mcp", new StringContent(new string('x', 65537), Encoding.UTF8, "application/json"))).StatusCode);
    }
}
