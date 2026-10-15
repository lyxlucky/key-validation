using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Api;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ManualTime : TimeProvider
{
    private long ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
}

public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private static readonly SemaphoreSlim DatabaseCleanup = new(1, 1);
    private string connection = "";
    private string baseConnection = "";
    private readonly string database = "keyvalidation_test_" + Guid.NewGuid().ToString("N");
    public readonly string Password = "T!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(20)) + "a";
    public readonly ManualTime Clock = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = connection,
            ["DataProtection:Path"] = Path.Combine(AppContext.BaseDirectory, "test-keys"),
            ["Licensing:CurrentKeyVersion"] = "v1",
            ["Licensing:Keys:v1"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Logging:LogLevel:Default"] = "Warning",
            ["RateLimiting:LoginPerMinute"] = "1000",
            ["Operations:WorkersEnabled"] = "false",
            ["Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command"] = "None"
        }));
        builder.ConfigureServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Clock));
            services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(AppContext.BaseDirectory, "test-keys")));
        });
    }
    public async Task InitializeAsync()
    {
        baseConnection = Environment.GetEnvironmentVariable("KEYVALIDATION_TEST_DATABASE") ?? "";
        if (string.IsNullOrEmpty(baseConnection))
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, ".local", "dev-secrets.json"))) directory = directory.Parent;
            if (directory is null) throw new InvalidOperationException("Real PostgreSQL required: set KEYVALIDATION_TEST_DATABASE. Integration tests do not skip.");
            using var secret = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(directory.FullName, ".local", "dev-secrets.json")));
            baseConnection = secret.RootElement.GetProperty("connectionString").GetString()!;
        }
        await using var admin = new NpgsqlConnection(baseConnection);
        await admin.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
        await command.ExecuteNonQueryAsync();
        var cs = new NpgsqlConnectionStringBuilder(baseConnection) { Database = database };
        connection = cs.ConnectionString;
        await using (var setup = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection).Options)) await setup.Database.MigrateAsync();
        using var scope = Services.CreateScope();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        foreach (var role in new[] { "Admin", "Operator", "ReadOnly" })
        {
            if (!await roles.RoleExistsAsync(role)) Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
            var user = new AdminUser { UserName = role.ToLowerInvariant() };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, role)).Succeeded);
        }
    }
    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (string.IsNullOrEmpty(baseConnection)) return;
        // Concurrent Windows database-directory deletion can exceed PostgreSQL's
        // normal command deadline. Serialize only teardown, retaining concurrent
        // HTTP tests and clearing no other fixture's connections.
        await DatabaseCleanup.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(connection))
            {
                using var ownedPool = new NpgsqlConnection(connection);
                NpgsqlConnection.ClearPool(ownedPool);
            }
            await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(baseConnection) { Pooling = false }.ConnectionString);
            await admin.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }
        finally { DatabaseCleanup.Release(); }
    }
    public async Task<HttpClient> Login(string user = "admin")
    {
        var client = CreateClient(); await Csrf(client);
        var response = await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(user, Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await Csrf(client); return client;
    }
    public static async Task Csrf(HttpClient client)
    {
        var response = await client.GetAsync("/api/admin/auth/csrf");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var csrf = await response.Content.ReadFromJsonAsync<CsrfDto>();
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
    }
    public async Task<(HttpClient Admin, ApplicationDto App, PlanDto Plan)> Catalog()
    {
        var admin = await Login();
        var appResponse = await admin.PostAsJsonAsync("/api/admin/applications", new CreateApplication("app-" + Guid.NewGuid().ToString("N"), "测试应用"));
        Assert.Equal(HttpStatusCode.Created, appResponse.StatusCode);
        var app = (await appResponse.Content.ReadFromJsonAsync<ApplicationDto>())!;
        var planResponse = await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("日卡", 86400, 1));
        Assert.Equal(HttpStatusCode.Created, planResponse.StatusCode);
        return (admin, app, (await planResponse.Content.ReadFromJsonAsync<PlanDto>())!);
    }
    public static async Task<IssuanceDto> Issue(HttpClient admin, ApplicationDto app, PlanDto plan)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        return (await response.Content.ReadFromJsonAsync<IssuanceDto>())!;
    }
}

public class ApiTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact] public async Task MalformedActivationConsumesGlobalAdmissionBeforeBodyBinding()
    {
        await using var limited = fixture.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["RateLimiting:GlobalPerMinute"] = "2" })));
        using var client = limited.CreateClient();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/client/licenses/activate", new StringContent("[]", System.Text.Encoding.UTF8, "application/json"))).StatusCode);
        var rejected = await client.PostAsync("/api/client/licenses/activate", new StringContent("[]", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
    }
    [Theory] [InlineData("[]")] [InlineData("null")] [InlineData("42")] [InlineData("\"text\"")]
    public async Task ActivationRejectsNonObjectJsonWithValidationProblem(string json)
    {
        using var client = fixture.CreateClient();
        var response = await client.PostAsync("/api/client/licenses/activate", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("VALIDATION_ERROR", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    [Fact] public async Task ActivationRateLimitUsesSameNormalizationAsApplicationLookup()
    {
        using var client = fixture.CreateClient(); var code = "norm-" + Guid.NewGuid().ToString("N")[..8];
        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++)
        {
            last?.Dispose();
            var body = new Dictionary<string, object> { [i % 2 == 0 ? "appCode" : "AppCode"] = new string(' ', i) + code.ToUpperInvariant() + " ", ["licenseKey"] = "invalid", ["deviceId"] = "device" };
            last = await client.PostAsJsonAsync("/api/client/licenses/activate", body);
        }
        using var response = last!;
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
    }
    [Fact] public async Task CliCreatesRoleAndResetsPasswordWithoutPasswordArguments()
    {
        var name = "cli-" + Guid.NewGuid().ToString("N");
        var original = Environment.GetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", fixture.Password);
            Assert.Equal(0, await AdminCli.Run(fixture.Services, ["admin", "create", name, "ReadOnly"]));
            Assert.Null(Environment.GetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD"));
            using var client = await fixture.Login(name);
            var created = await client.GetFromJsonAsync<JsonElement>("/api/admin/auth/me");
            Assert.True(created.GetProperty("mustChangePassword").GetBoolean());
            Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", fixture.Password);
            Assert.Equal(0, await AdminCli.Run(fixture.Services, ["admin", "reset-password", name]));
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(await db.AuditLogs.AnyAsync(x => x.ActorId == "cli" && x.Action == "admin.reset-password"));
        }
        finally { Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", original); }
    }
    [Fact] public async Task ProductionCookiesAreSecureAndUntrustedForwardedHeadersDoNotBypassHttps()
    {
        await using var production = fixture.WithWebHostBuilder(b => b.UseEnvironment("Production"));
        using var insecure = production.CreateClient(new() { BaseAddress = new Uri("http://localhost"), AllowAutoRedirect = false });
        insecure.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await insecure.GetAsync("/api/admin/auth/csrf")).StatusCode);
        using var secure = production.CreateClient(new() { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });
        await ApiFixture.Csrf(secure);
        var response = await secure.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("KeyValidation.Admin=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        await ApiFixture.Csrf(secure);
        Assert.Equal(HttpStatusCode.NoContent, (await secure.PostAsync("/api/admin/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await secure.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact] public async Task CliBootstrapAdminRetainsAccessButResetRequiresPasswordChange()
    {
        var name = "cli-admin-" + Guid.NewGuid().ToString("N");
        var original = Environment.GetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD");
        try
        {
            Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", fixture.Password);
            Assert.Equal(0, await AdminCli.Run(fixture.Services, ["admin", "create", name, "Admin"]));
            using var bootstrap = await fixture.Login(name);
            Assert.False((await bootstrap.GetFromJsonAsync<JsonElement>("/api/admin/auth/me")).GetProperty("mustChangePassword").GetBoolean());
            Assert.Equal(HttpStatusCode.OK, (await bootstrap.GetAsync("/api/admin/accounts")).StatusCode);
            Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", fixture.Password);
            Assert.Equal(0, await AdminCli.Run(fixture.Services, ["admin", "reset-password", name]));
            Assert.Equal(HttpStatusCode.Unauthorized, (await bootstrap.GetAsync("/api/admin/auth/me")).StatusCode);
            using var reset = await fixture.Login(name);
            Assert.True((await reset.GetFromJsonAsync<JsonElement>("/api/admin/auth/me")).GetProperty("mustChangePassword").GetBoolean());
            Assert.Equal(HttpStatusCode.Forbidden, (await reset.GetAsync("/api/admin/accounts")).StatusCode);
        }
        finally { Environment.SetEnvironmentVariable("KEYVALIDATION_ADMIN_PASSWORD", original); }
    }
    [Fact] public async Task ActivationIsRateLimitedWithoutLockingTheLicense()
    {
        using var client = fixture.CreateClient(); var code = "rate-" + Guid.NewGuid().ToString("N");
        HttpResponseMessage? last = null;
        for (var i = 0; i < 31; i++) { last?.Dispose(); last = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(code, "invalid", "device")); }
        using var response = last!;
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.Equal("RATE_LIMITED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
    [Fact] public async Task ConcurrentDisableAndHeartbeatCannotLeaveAUsableSessionAfterCommit()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var session = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"))).Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        var disable = admin.PostAsync($"/api/admin/licenses/{batch.Licenses[0].Id}/disable", null);
        var heartbeat = client.PostAsync("/api/client/sessions/heartbeat", null);
        await Task.WhenAll(disable, heartbeat);
        Assert.Equal(HttpStatusCode.NoContent, (await disable).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.NotNull(Assert.Single(Assert.Single(detail.Bindings).Sessions).RevokedAt);
    }
    [Fact] public async Task RetentionDeletesOnlyOldHistory()
    {
        var now = fixture.Clock.GetUtcNow(); var auditId = Guid.NewGuid(); var eventId = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.Add(new() { Id = auditId, ActorId = "retention-test", Action = "test", CreatedAt = now.AddDays(-181), TraceId = "test" });
            db.AuthorizationEvents.Add(new() { Id = eventId, Outcome = "test", CreatedAt = now.AddDays(-31) });
            await db.SaveChangesAsync();
        }
        using var worker = new RetentionWorker(fixture.Services.GetRequiredService<IServiceScopeFactory>(), fixture.Clock, fixture.Services.GetRequiredService<IConfiguration>(), NullLogger<RetentionWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);
        var removed = false;
        for (var i = 0; i < 40; i++)
        {
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            removed = !await db.AuditLogs.AnyAsync(x => x.Id == auditId) && !await db.AuthorizationEvents.AnyAsync(x => x.Id == eventId);
            if (removed) break;
            await Task.Delay(25);
        }
        await worker.StopAsync(CancellationToken.None); Assert.True(removed);
        using var finalScope = fixture.Services.CreateScope();
        Assert.True(await finalScope.ServiceProvider.GetRequiredService<AppDbContext>().Users.AnyAsync());
    }
    [Fact] public async Task LeaseBoundaryIsEnforcedThroughHttpAndCanRecoverByActivation()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var request = new ActivateLicense(app.Code, batch.Licenses[0].Key, "device");
        var first = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", request)).Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        fixture.Clock.Advance(TimeSpan.FromSeconds(180));
        var response = await client.PostAsync("/api/client/sessions/heartbeat", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("SESSION_EXPIRED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var second = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", request)).Content.ReadFromJsonAsync<ActivationDto>())!;
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
    }
    [Fact] public async Task OpenApiContainsTypedIssuanceAndActivationContracts()
    {
        using var client = fixture.CreateClient();
        var document = await client.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        var paths = document.GetProperty("paths");
        Assert.True(paths.GetProperty("/api/admin/license-batches").GetProperty("post").GetProperty("requestBody").GetProperty("content").GetProperty("application/json").TryGetProperty("schema", out _));
        Assert.True(paths.GetProperty("/api/client/licenses/activate").GetProperty("post").GetProperty("responses").TryGetProperty("200", out _));
        Assert.True(document.GetProperty("components").GetProperty("schemas").TryGetProperty("ActivationDto", out _));
        Assert.True(document.GetProperty("components").TryGetProperty("securitySchemes", out _));
        Assert.True(paths.GetProperty("/api/admin/license-batches").GetProperty("post").GetProperty("responses").TryGetProperty("409", out _));
    }
    [Fact] public async Task AdminIdleAndAbsoluteExpiryCannotBeExtendedIndefinitely()
    {
        using var idle = await fixture.Login();
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(HttpStatusCode.Unauthorized, (await idle.GetAsync("/api/admin/auth/me")).StatusCode);
        using var active = await fixture.Login();
        for (var step = 0; step < 23; step++)
        {
            fixture.Clock.Advance(TimeSpan.FromMinutes(20));
            Assert.Equal(HttpStatusCode.OK, (await active.GetAsync("/api/admin/auth/me")).StatusCode);
        }
        fixture.Clock.Advance(TimeSpan.FromMinutes(20));
        Assert.Equal(HttpStatusCode.Unauthorized, (await active.GetAsync("/api/admin/auth/me")).StatusCode);
    }
    [Fact] public async Task PasswordResetInvalidatesExistingCookie()
    {
        using var client = await fixture.Login("operator");
        using var scope = fixture.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = (await users.FindByNameAsync("operator"))!;
        var token = await users.GeneratePasswordResetTokenAsync(user);
        Assert.True((await users.ResetPasswordAsync(user, token, fixture.Password)).Succeeded);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/auth/me")).StatusCode);
    }
    [Fact] public async Task InvalidBoundsReturnProblemDetailsAndLogoutPreservesBinding()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var invalid = await admin.GetAsync("/api/admin/licenses?pageSize=101");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType!.MediaType);
        Assert.True((await invalid.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("traceId", out _));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1001, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new { name = "incomplete", durationSeconds = 86400, maxDevices = 1 })).StatusCode);
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var activated = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"))).Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", activated.SessionToken);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/client/sessions/logout", null)).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Null(Assert.Single(detail.Bindings).ReleasedAt);
        Assert.NotNull(Assert.Single(detail.Bindings[0].Sessions).RevokedAt);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
    }
    [Fact] public async Task AnonymousAndMissingCsrfCannotMutateAdmin()
    {
        using var anonymous = fixture.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/admin/applications")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
        using var admin = await fixture.Login(); admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/applications", new CreateApplication("bad", "x"))).StatusCode);
    }
    [Fact] public async Task RolesAreEnforcedByHttpApi()
    {
        using var op = await fixture.Login("operator"); using var reader = await fixture.Login("readonly");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsJsonAsync("/api/admin/applications", new CreateApplication("bad", "x"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(Guid.NewGuid(), Guid.NewGuid(), 1, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reader.GetAsync("/api/admin/licenses")).StatusCode);
    }
    [Fact] public async Task ConcurrentTwoDeviceActivationCannotExceedCapacity()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device-" + i))));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        var failed = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Forbidden);
        Assert.Equal("DEVICE_LIMIT_REACHED", (await failed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var detail = await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}");
        Assert.Single(detail!.Bindings);
    }
    [Fact] public async Task DuplicateIssuanceHasOneWinnerAndResponseLossCanBeRecovered()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var request = new IssueBatch(app.Id, plan.Id, 3, "race", Guid.NewGuid());
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.PostAsJsonAsync("/api/admin/license-batches", request)));
        var success = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        var batch = (await success.Content.ReadFromJsonAsync<IssuanceDto>())!;
        var conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("BATCH_ALREADY_CREATED", problem.GetProperty("code").GetString());
        Assert.Equal(batch.Batch.Id, problem.GetProperty("batchId").GetGuid());
        var changed = await admin.PostAsJsonAsync("/api/admin/license-batches", request with { Quantity = 4 });
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await changed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        var found = await admin.GetFromJsonAsync<PagedResult<BatchDto>>($"/api/admin/license-batches?requestId={request.RequestId}");
        Assert.Equal(batch.Batch.Id, Assert.Single(found!.Items).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/license-batches/{batch.Batch.Id}/disable", null)).StatusCode);
        var licenses = await admin.GetFromJsonAsync<PagedResult<LicenseDto>>($"/api/admin/licenses?batchId={batch.Batch.Id}");
        Assert.Equal(3, licenses!.Total); Assert.All(licenses.Items, x => Assert.Equal("disabled", x.Status));
    }
    [Theory] [InlineData("license")] [InlineData("application")] [InlineData("binding")] [InlineData("session")]
    public async Task RevocationIsImmediateAndEnableNeverRevivesOldSession(string target)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var activated = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, batch.Licenses[0].Key, "device"));
        var session = (await activated.Content.ReadFromJsonAsync<ActivationDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.SessionToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        var path = target switch { "license" => $"licenses/{detail.License.Id}/disable", "application" => $"applications/{app.Id}/disable", "binding" => $"device-bindings/{detail.Bindings[0].Id}/release", _ => $"sessions/{detail.Bindings[0].Sessions[0].Id}/revoke" };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync("/api/admin/" + path, null)).StatusCode);
        Assert.False((await client.PostAsync("/api/client/sessions/heartbeat", null)).IsSuccessStatusCode);
        if (target is "license" or "application") await admin.PostAsync("/api/admin/" + path.Replace("disable", "enable"), null);
        Assert.False((await client.PostAsync("/api/client/sessions/heartbeat", null)).IsSuccessStatusCode);
    }
    [Fact] public async Task RepeatedActivationPreservesExpiryAndReplacesSession()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan); using var client = fixture.CreateClient();
        var request = new ActivateLicense(app.Code, batch.Licenses[0].Key, "device");
        var first = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", request)).Content.ReadFromJsonAsync<ActivationDto>())!;
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));
        var second = (await (await client.PostAsJsonAsync("/api/client/licenses/activate", request)).Content.ReadFromJsonAsync<ActivationDto>())!;
        Assert.Equal(first.ExpiresAt, second.ExpiresAt);
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/client/sessions/heartbeat", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/licenses")).StatusCode);
    }
    [Fact] public async Task PlaintextNeverAppearsInQueriesOrAuditsAndPlansAreSnapshots()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var adminDispose = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("changed", 3600, 10, false))).StatusCode);
        var detail = (await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{batch.Licenses[0].Id}"))!;
        Assert.Equal(86400, detail.License.DurationSeconds); Assert.Equal(1, detail.License.MaxDevices);
        var search = await admin.PostAsJsonAsync("/api/admin/licenses/search", new SearchLicense(batch.Licenses[0].Key.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        foreach (var path in new[] { "/api/admin/licenses", "/api/admin/license-batches", "/api/admin/audit-logs" })
        {
            var text = await admin.GetStringAsync(path); Assert.DoesNotContain(batch.Licenses[0].Key, text); Assert.DoesNotContain("keyDigest", text); Assert.DoesNotContain("tokenDigest", text);
        }
        var audit = await admin.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={batch.Batch.Id}");
        Assert.Contains(audit!.Items, x => x.Action == "batch.create");
    }
}
