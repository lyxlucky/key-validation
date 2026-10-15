using System.Net;
using System.Text;
using System.Text.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class LicenseApiPolicyTests
{
    [Fact]
    public async Task TrialRequestOmitsLicenseKeyAndReadsGrantedCapabilities()
    {
        using var http = new HttpClient(new Reply(request =>
        {
            Assert.Equal("/api/client/trials/activate", request.RequestUri!.AbsolutePath);
            Assert.Null(request.Headers.Authorization);
            using var json = JsonDocument.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            Assert.Equal("product", json.RootElement.GetProperty("appCode").GetString());
            Assert.Equal("device", json.RootElement.GetProperty("deviceId").GetString());
            Assert.False(json.RootElement.TryGetProperty("licenseKey", out _));
            return Json("""
                {"serverTime":"2026-10-06T00:00:00Z","expiresAt":"2026-10-06T01:00:00Z",
                 "leaseExpiresAt":"2026-10-06T00:03:00Z","absoluteExpiresAt":"2026-10-06T01:00:00Z",
                 "heartbeatIntervalSeconds":60,"sessionToken":"trial-token",
                 "licenseId":"11111111-1111-1111-1111-111111111111","isTrial":true,"features":["preview"]}
                """);
        })) { BaseAddress = new Uri("https://example.test") };
        var grant = await new LicenseApi(http).ActivateTrialAsync("product", "device", "Desktop", CancellationToken.None);
        Assert.True(grant.IsTrial);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), grant.LicenseId);
        Assert.Equal(new[] { "preview" }, grant.Features);
    }

    [Fact]
    public async Task OldServerGrantDoesNotImplicitlyGrantFeatures()
    {
        using var http = new HttpClient(new Reply(_ => Json("""
            {"serverTime":"2026-10-06T00:00:00Z","expiresAt":"2026-10-06T01:00:00Z",
             "leaseExpiresAt":"2026-10-06T00:03:00Z","absoluteExpiresAt":"2026-10-06T01:00:00Z",
             "heartbeatIntervalSeconds":60,"sessionToken":"old-token"}
            """))) { BaseAddress = new Uri("https://example.test") };
        var grant = await new LicenseApi(http).ActivateAsync("product", "key", "device", "Desktop", CancellationToken.None);
        Assert.Null(grant.Features);
        Assert.False(grant.IsTrial);
        Assert.Equal("old-token", grant.SessionToken);
    }

    [Fact]
    public async Task TrialDenialPropagatesTheServerCode()
    {
        using var http = new HttpClient(new Reply(_ => new(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"code\":\"TRIAL_UPGRADED\"}", Encoding.UTF8, "application/problem+json")
        })) { BaseAddress = new Uri("https://example.test") };
        var failure = await Assert.ThrowsAsync<LicenseRejectedException>(() => new LicenseApi(http).ActivateTrialAsync("product", "device", "Desktop", CancellationToken.None));
        Assert.Equal("TRIAL_UPGRADED", failure.Message);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Reply(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(reply(request));
    }
}
