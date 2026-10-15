using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityMonitoringTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task MonitoringCountsActualClientRequestsAndUsesFixedRoutes()
    {
        await using var host = fixture.WithWebHostBuilder(_ => { }); using var admin = await Login(host);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/monitoring/sample", null)).StatusCode);
        using var client = host.CreateClient();
        for (var i = 0; i < 3; i++)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/client/licenses/activate?unbounded=" + Guid.NewGuid(), new ActivateLicense("missing", "invalid", "metrics-device"))).StatusCode);
        var sampled = (await (await admin.PostAsync("/api/admin/monitoring/sample", null)).Content.ReadFromJsonAsync<MonitoringSampleDto>())!;
        Assert.Equal(3, sampled.RequestCount); Assert.Equal(0, sampled.ServerErrorCount); Assert.Equal(0, sampled.ErrorRate); Assert.NotNull(sampled.P95LatencyMs);
        Assert.Equal("/api/client/licenses/activate", Assert.Single(sampled.Routes).Route);
        var metrics = await admin.GetStringAsync("/api/admin/monitoring/metrics"); Assert.DoesNotContain("unbounded", metrics);
        Assert.Contains("keygate_validation_requests_total{route=\"/api/client/licenses/activate\"} 3", metrics);
    }

    [Fact]
    public async Task ThresholdsRequireThreeSamplesThenPersistOneIncidentAndOneRecovery()
    {
        await using var host = fixture.WithWebHostBuilder(_ => { }); using var admin = await Login(host); Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/monitoring")).StatusCode);
        var metrics = host.Services.GetRequiredService<IValidationRequestMetrics>();
        for (var sample = 0; sample < 3; sample++)
        {
            for (var i = 0; i < 20; i++) metrics.Record("/api/client/licenses/activate", i == 0 ? 500 : 200, 600);
            fixture.Clock.Advance(TimeSpan.FromMinutes(1));
            var response = await admin.PostAsync("/api/admin/monitoring/sample", null); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var data = (await response.Content.ReadFromJsonAsync<MonitoringSampleDto>())!; Assert.Equal(0.05, data.ErrorRate); Assert.Equal(600, data.P95LatencyMs);
            var snapshot = (await admin.GetFromJsonAsync<MonitoringDto>("/api/admin/monitoring"))!;
            Assert.Equal(sample == 2 ? 2 : 0, snapshot.Incidents.Count(x => x.ResolvedAt is null));
        }
        for (var i = 0; i < 20; i++) metrics.Record("/api/client/licenses/activate", 200, 20);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1)); Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/monitoring/sample", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/monitoring/sample", null)).StatusCode);
        var recovered = (await admin.GetFromJsonAsync<MonitoringDto>("/api/admin/monitoring"))!;
        Assert.Equal(2, recovered.Incidents.Count(x => x.Kind is "error_rate" or "high_latency")); Assert.All(recovered.Incidents, x => Assert.NotNull(x.ResolvedAt));
    }

    [Fact]
    public async Task FailedDatabaseProbeKeepsAnOutageInMemoryAndPersistsItAfterRecovery()
    {
        var connection = fixture.Services.GetRequiredService<IConfiguration>().GetConnectionString("Database")!;
        var probe = new RoutingDatabaseProbe(connection) { Unavailable = true };
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<IDatabaseAvailabilityProbe>(probe))));
        using var admin = await Login(host);
        var down = await admin.PostAsync("/api/admin/monitoring/sample", null); Assert.Equal(HttpStatusCode.OK, down.StatusCode);
        Assert.False((await down.Content.ReadFromJsonAsync<MonitoringSampleDto>())!.DatabaseAvailable);
        using (var scope = fixture.Services.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().MonitoringIncidents.AnyAsync(x => x.Kind == "database_unavailable"));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1)); probe.Unavailable = false;
        var up = await admin.PostAsync("/api/admin/monitoring/sample", null); Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.True((await up.Content.ReadFromJsonAsync<MonitoringSampleDto>())!.DatabaseAvailable);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/monitoring/sample", null)).StatusCode);
        var restored = (await admin.GetFromJsonAsync<MonitoringDto>("/api/admin/monitoring"))!;
        var incident = Assert.Single(restored.Incidents, x => x.Kind == "database_unavailable"); Assert.NotNull(incident.ResolvedAt);
        Assert.Contains(restored.Samples, x => !x.DatabaseAvailable);
    }

    private async Task<HttpClient> Login(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
        await ApiFixture.Csrf(client); return client;
    }

    [Fact]
    public async Task AnActualUnhandledClientFailureIncrementsTheFiveHundredCounter()
    {
        var real = fixture.Services.GetRequiredService<ISecretService>();
        await using var host = fixture.WithWebHostBuilder(builder => builder.ConfigureServices(services => services.Replace(ServiceDescriptor.Singleton<ISecretService>(new FailingDigest(real)))));
        using var admin = await Login(host); using var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense("fault", "anything", "device"));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var sample = (await (await admin.PostAsync("/api/admin/monitoring/sample", null)).Content.ReadFromJsonAsync<MonitoringSampleDto>())!;
        Assert.Equal(1, sample.RequestCount); Assert.Equal(1, sample.ServerErrorCount); Assert.Equal(1, sample.ErrorRate);
    }

    private sealed class FailingDigest(ISecretService inner) : ISecretService
    {
        public (string Digest, string Version) DigestLicense(string key) => throw new InvalidOperationException("Deliberate test failure.");
        public GeneratedKey GenerateLicense() => inner.GenerateLicense();
        public string DigestDevice(Guid applicationId, string deviceId) => inner.DigestDevice(applicationId, deviceId);
        public (string Token, string Digest) GenerateSession() => inner.GenerateSession();
        public string DigestToken(string token) => inner.DigestToken(token);
    }
}

internal sealed class RoutingDatabaseProbe(string connectionString) : IDatabaseAvailabilityProbe
{
    public bool Unavailable { get; set; }
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        var cs = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false };
        if (Unavailable) cs.Database = "missing_probe_" + Guid.NewGuid().ToString("N");
        try { await using var connection = new NpgsqlConnection(cs.ConnectionString); await connection.OpenAsync(cancellationToken); await using var command = new NpgsqlCommand("SELECT 1", connection); await command.ExecuteScalarAsync(cancellationToken); return true; }
        catch (NpgsqlException) { return false; }
    }
}
