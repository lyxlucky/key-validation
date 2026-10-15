using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityWebhookTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task SignedDeliveryRetriesTheIdenticalEventBodyAndSerializesConcurrentClaims()
    {
        await using var receiver = new LocalWebhookReceiver { Status = 503 };
        await using var host = Host(receiver.Url);
        using var admin = await Login(host);
        var app = (await (await admin.PostAsJsonAsync("/api/admin/applications", new CreateApplication("hooks-" + Guid.NewGuid().ToString("N"), "Hooks"))).Content.ReadFromJsonAsync<ApplicationDto>())!;
        var plan = (await (await admin.PostAsJsonAsync($"/api/admin/applications/{app.Id}/plans", new CreatePlan("Plan", 86400, 1))).Content.ReadFromJsonAsync<PlanDto>())!;
        var response = await admin.PostAsJsonAsync("/api/admin/webhook-subscriptions", new CreateWebhookSubscription(app.Id, "Local receiver", receiver.Url, ["batch.completed"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var secret = (await response.Content.ReadFromJsonAsync<WebhookSecretDto>())!;
        Assert.Equal(32, Convert.FromBase64String(secret.Secret).Length);
        var ordinary = await admin.GetStringAsync("/api/admin/webhook-subscriptions");
        Assert.DoesNotContain(secret.Secret, ordinary); Assert.DoesNotContain("protectedSecret", ordinary);
        await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var attempted = await Task.WhenAll(Deliver(host), Deliver(host));
        Assert.Equal(1, attempted.Sum()); Assert.Single(receiver.Requests);
        var first = receiver.Requests.Single();
        var expected = Convert.ToHexString(HMACSHA256.HashData(Convert.FromBase64String(secret.Secret), Encoding.UTF8.GetBytes(first.Headers["X-KeyGate-Timestamp"] + "\n" + first.Body))).ToLowerInvariant();
        Assert.Equal(expected, first.Headers["X-KeyGate-Signature"]);
        Assert.Equal(JsonDocument.Parse(first.Body).RootElement.GetProperty("id").GetString(), first.Headers["X-KeyGate-Event-Id"]);
        var delivery = Assert.Single((await admin.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>($"/api/admin/webhook-deliveries?subscriptionId={secret.Subscription.Id}"))!.Items);
        Assert.Equal("pending", delivery.Status); Assert.Equal(1, delivery.Attempts); Assert.Equal("HTTP_503", delivery.LastError);
        Assert.Equal(0, await Deliver(host));
        fixture.Clock.Advance(TimeSpan.FromMinutes(1)); receiver.Status = 204;
        Assert.Equal(1, await Deliver(host)); Assert.Equal(2, receiver.Requests.Count);
        var second = receiver.Requests.Last(); Assert.Equal(first.Body, second.Body); Assert.Equal(first.Headers["X-KeyGate-Event-Id"], second.Headers["X-KeyGate-Event-Id"]);
        delivery = Assert.Single((await admin.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>($"/api/admin/webhook-deliveries?subscriptionId={secret.Subscription.Id}"))!.Items);
        Assert.Equal("succeeded", delivery.Status); Assert.Equal(2, delivery.Attempts); Assert.NotNull(delivery.CompletedAt);
        var rotated = (await (await admin.PostAsync($"/api/admin/webhook-subscriptions/{secret.Subscription.Id}/rotate-secret", null)).Content.ReadFromJsonAsync<WebhookSecretDto>())!;
        Assert.NotEqual(secret.Secret, rotated.Secret);
    }

    [Fact]
    public async Task AutomaticDeliveryStopsAfterFiveFailuresAndManualRetryKeepsTheEventId()
    {
        await using var receiver = new LocalWebhookReceiver { Status = 500 };
        await using var host = Host(receiver.Url); using var admin = await Login(host);
        var (baseAdmin, app, plan) = await fixture.Catalog(); using var cleanup = baseAdmin;
        var response = await admin.PostAsJsonAsync("/api/admin/webhook-subscriptions", new CreateWebhookSubscription(app.Id, "Failure receiver", receiver.Url, ["batch.completed"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var subscription = (await response.Content.ReadFromJsonAsync<WebhookSecretDto>())!.Subscription;
        await ApiFixture.Issue(admin, app, plan); Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        foreach (var minutes in new[] { 0, 1, 5, 30, 120 }) { fixture.Clock.Advance(TimeSpan.FromMinutes(minutes)); Assert.Equal(1, await Deliver(host)); }
        Assert.Equal(0, await Deliver(host)); Assert.Equal(5, receiver.Requests.Count);
        using var fresh = await Login(host);
        var failed = Assert.Single((await fresh.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>($"/api/admin/webhook-deliveries?subscriptionId={subscription.Id}"))!.Items);
        Assert.Equal("failed", failed.Status); Assert.Equal(5, failed.Attempts);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsync($"/api/admin/webhook-deliveries/{failed.Id}/retry", null)).StatusCode);
        receiver.Status = 200; Assert.Equal(1, await Deliver(host));
        var succeeded = Assert.Single((await fresh.GetFromJsonAsync<PagedResult<WebhookDeliveryDto>>($"/api/admin/webhook-deliveries?subscriptionId={subscription.Id}"))!.Items);
        Assert.Equal(failed.EventId, succeeded.EventId); Assert.Equal("succeeded", succeeded.Status);
        Assert.Single(receiver.Requests.Select(x => x.Headers["X-KeyGate-Event-Id"]).Distinct());
    }

    [Fact]
    public async Task PublisherParticipatesInTheCallersRollbackAndReusesTheStableId()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        Guid eventId;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var publisher = scope.ServiceProvider.GetRequiredService<IDurableEventPublisher>();
            await using var tx = await db.Database.BeginTransactionAsync();
            eventId = await publisher.PublishAsync(app.Id, app.Id, "order.fulfilled", "{\"quantity\":1}", "rollback-fixture");
            Assert.Equal(eventId, await publisher.PublishAsync(app.Id, app.Id, "order.fulfilled", "{\"quantity\":1}", "rollback-fixture"));
            await db.SaveChangesAsync(); await tx.RollbackAsync();
        }
        using var check = fixture.Services.CreateScope(); var verification = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verification.OperationsEvents.AnyAsync(x => x.Id == eventId));
    }

    private WebApplicationFactory<Program> Host(string url) => fixture.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
        new Dictionary<string, string?> { ["Webhooks:AllowLoopbackHttp"] = "true", ["Webhooks:AllowedHosts:0"] = new Uri(url).Host, ["Operations:WorkersEnabled"] = "false" })));
    private async Task<HttpClient> Login(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient(); await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
        await ApiFixture.Csrf(client); return client;
    }
    private static async Task<int> Deliver(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<IWebhookDeliveryProcessor>().DeliverDueAsync();
    }
}

internal sealed class LocalWebhookReceiver : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly Task serve;
    public string Url { get; }
    public int Status { get; set; } = 200;
    public ConcurrentQueue<ReceivedWebhook> Requests { get; } = new();
    public LocalWebhookReceiver()
    {
        listener.Start(); Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/events"; serve = Serve();
    }
    private async Task Serve()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token); using var stream = client.GetStream();
                var header = new List<byte>(); var single = new byte[1];
                while (header.Count < 16384)
                {
                    await stream.ReadExactlyAsync(single, stop.Token); header.Add(single[0]);
                    if (header.Count >= 4 && Encoding.ASCII.GetString(header.Skip(header.Count - 4).ToArray()) == "\r\n\r\n") break;
                }
                var headers = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n").Skip(1).Where(line => line.Contains(':'))
                    .Select(line => line.Split(':', 2)).ToDictionary(pair => pair[0], pair => pair[1].Trim(), StringComparer.OrdinalIgnoreCase);
                var body = new byte[int.Parse(headers["Content-Length"])]; await stream.ReadExactlyAsync(body, stop.Token);
                Requests.Enqueue(new(headers, Encoding.UTF8.GetString(body)));
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 {Status} Result\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    public async ValueTask DisposeAsync() { stop.Cancel(); listener.Stop(); await serve; stop.Dispose(); }
}
internal sealed record ReceivedWebhook(Dictionary<string, string> Headers, string Body);
