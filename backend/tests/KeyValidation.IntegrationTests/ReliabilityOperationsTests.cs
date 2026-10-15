using System.Net;
using System.Net.Http.Json;
using KeyValidation.Application;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityOperationsTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task UnconfiguredBackupPersistsFailureAndDoesNotPretendToBeReady()
    {
        using var admin = await fixture.Login();
        var response = await admin.GetAsync("/api/admin/backups/status");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = (await response.Content.ReadFromJsonAsync<BackupStatusDto>())!;
        Assert.False(status.Enabled); Assert.False(status.Configured); Assert.False(status.ScheduleReady);
        Assert.NotEmpty(status.ConfigurationErrors);
        var create = await admin.PostAsync("/api/admin/backups", null);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var job = (await create.Content.ReadFromJsonAsync<BackupJobDto>())!;
        Assert.Equal("failed", job.Status); Assert.NotNull(job.ErrorCode);
        Assert.Contains((await admin.GetFromJsonAsync<PagedResult<BackupJobDto>>("/api/admin/backups"))!.Items, x => x.Id == job.Id && x.Status == "failed");
        using var reader = await fixture.Login("readonly");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/admin/backups/status")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsync("/api/admin/backups", null)).StatusCode);
    }

    [Fact]
    public async Task MonitoringWithoutClientTrafficReportsNoLatencyOrErrorSample()
    {
        using var admin = await fixture.Login();
        var response = await admin.PostAsync("/api/admin/monitoring/sample", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var sample = (await response.Content.ReadFromJsonAsync<MonitoringSampleDto>())!;
        Assert.True(sample.DatabaseAvailable); Assert.Equal(0, sample.RequestCount);
        Assert.Null(sample.ErrorRate); Assert.Null(sample.P95LatencyMs);
        var monitor = (await admin.GetFromJsonAsync<MonitoringDto>("/api/admin/monitoring"))!;
        Assert.True(monitor.DatabaseAvailable); Assert.NotNull(monitor.Latest);
        var metrics = await admin.GetAsync("/api/admin/monitoring/metrics");
        Assert.Equal(HttpStatusCode.OK, metrics.StatusCode);
        Assert.StartsWith("text/plain", metrics.Content.Headers.ContentType!.ToString());
        Assert.Contains("keygate_database_available 1", await metrics.Content.ReadAsStringAsync());
        using var reader = await fixture.Login("readonly");
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.GetAsync("/api/admin/monitoring")).StatusCode);
    }

    [Fact]
    public async Task WebhookConfigurationRejectsPrivateAndUnapprovedDestinations()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        foreach (var url in new[] { "http://127.0.0.1:9876/events", "https://127.0.0.1/events", "https://unapproved.example/events", "file:///secret" })
        {
            var response = await admin.PostAsJsonAsync("/api/admin/webhook-subscriptions", new CreateWebhookSubscription(app.Id, "Unsafe", url, ["risk.detected"]));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var operatorClient = await fixture.Login("operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/admin/webhook-subscriptions")).StatusCode);
    }
}
