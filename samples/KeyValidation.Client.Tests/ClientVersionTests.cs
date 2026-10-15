using System.Net;
using System.Text;
using System.Text.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class ClientVersionTests
{
    [Fact]
    public async Task ConfiguredVersionIsSentForFormalTrialAndHeartbeatAndUpdateMetadataIsReturned()
    {
        using var http = new HttpClient(new Reply(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal)) Assert.Equal("2.9.0", Assert.Single(request.Headers.GetValues("X-Client-Version")));
            else
            {
                using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
                Assert.Equal("2.9.0", json.RootElement.GetProperty("clientVersion").GetString());
            }
            return new(HttpStatusCode.OK) { Content = new StringContent("""
                {"serverTime":"2026-10-09T12:00:00Z","expiresAt":"2026-10-09T13:00:00Z",
                 "leaseExpiresAt":"2026-10-09T12:03:00Z","absoluteExpiresAt":"2026-10-09T13:00:00Z",
                 "heartbeatIntervalSeconds":60,"sessionToken":"token","features":["basic"],
                 "version":{"clientVersion":"2.9.0","minimumVersion":"2.9.0","latestVersion":"2.10.0",
                 "enforceMinimum":true,"upgradeAvailable":true,"updateUrl":"https://updates.example.test/client.exe","releaseNotes":"Updates","sha256":null}}
                """, Encoding.UTF8, "application/json") };
        })) { BaseAddress = new Uri("https://example.test") };
        var api = new LicenseApi(http, "2.9.0");
        var formal = await api.ActivateAsync("product", "key", "device", "Desktop", CancellationToken.None);
        var trial = await api.ActivateTrialAsync("product", "device", "Desktop", CancellationToken.None);
        var heartbeat = await api.HeartbeatAsync("token", CancellationToken.None);
        foreach (var grant in new[] { formal, trial, heartbeat }) { Assert.NotNull(grant.Version); Assert.True(grant.Version.UpgradeAvailable); Assert.Equal("2.10.0", grant.Version.LatestVersion); }
    }

    [Fact]
    public async Task UpgradeRejectionCarriesActionableMetadataWithoutDownloadingAnything()
    {
        using var http = new HttpClient(new Reply(_ => new(HttpStatusCode.UpgradeRequired)
        {
            Content = new StringContent("""
                {"code":"CLIENT_UPGRADE_REQUIRED","version":{"clientVersion":"2.8.0","minimumVersion":"2.9.0","latestVersion":"2.10.0","enforceMinimum":true,
                 "upgradeAvailable":true,"updateUrl":"https://updates.example.test/client.exe","releaseNotes":"Updates","sha256":null}}
                """, Encoding.UTF8, "application/problem+json")
        })) { BaseAddress = new Uri("https://example.test") };
        var error = await Assert.ThrowsAsync<LicenseRejectedException>(() => new LicenseApi(http, "2.8.0").ActivateAsync("product", "key", "device", "Desktop", CancellationToken.None));
        Assert.Equal("CLIENT_UPGRADE_REQUIRED", error.Message); Assert.Equal("2.9.0", error.Version?.MinimumVersion); Assert.Equal("https://updates.example.test/client.exe", error.Version?.UpdateUrl);
    }
    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request)); }
}
