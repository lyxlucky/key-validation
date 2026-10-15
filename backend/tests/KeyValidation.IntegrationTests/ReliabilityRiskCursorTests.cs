using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class ReliabilityRiskCursorTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Theory]
    [InlineData("activation_burst", 1)]
    [InlineData("rebinding_burst", 1)]
    [InlineData("country_change", 60)]
    public async Task AWriterHeldAcrossMultipleScanAttemptsCannotBePassedByTheCursor(string rule, int minutes)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan); var licenseId = issued.Licenses[0].Id;
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy",
            new UpdateRiskPolicy(true, rule == "activation_burst" ? 2 : 10000, 1, 2, 1, 1))).StatusCode);
        var firstAt = fixture.Clock.GetUtcNow();
        var pendingBinding = new DeviceBinding { LicenseId = licenseId, DeviceDigest = "pending-" + Guid.NewGuid().ToString("N"), BoundAt = firstAt };
        using (var setupScope = fixture.Services.CreateScope())
        {
            var setup = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (rule == "rebinding_burst")
                setup.Bindings.AddRange(new DeviceBinding { LicenseId = licenseId, DeviceDigest = "released-" + Guid.NewGuid().ToString("N"), BoundAt = firstAt, ReleasedAt = firstAt }, pendingBinding);
            else
                setup.AuthorizationEvents.Add(new AuthorizationEvent { ApplicationId = app.Id, LicenseId = licenseId, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = firstAt, TrustedCountryCode = rule == "country_change" ? "US" : null });
            await setup.SaveChangesAsync();
        }
        fixture.Clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10));
        using var pendingScope = fixture.Services.CreateScope(); var pending = pendingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await pending.Database.BeginTransactionAsync();
        // This is the existing licensing writer contract: take the application
        // FOR SHARE lock before obtaining the fact timestamp, through commit.
        await pending.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {app.Id} FOR SHARE");
        var factNow = fixture.Clock.GetUtcNow();
        var factAt = new DateTimeOffset(factNow.UtcTicks - factNow.UtcTicks % 10, TimeSpan.Zero);
        if (rule == "rebinding_burst")
            (await pending.Bindings.SingleAsync(x => x.Id == pendingBinding.Id)).ReleasedAt = factAt;
        else
            pending.AuthorizationEvents.Add(new AuthorizationEvent { ApplicationId = app.Id, LicenseId = licenseId, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = factAt, TrustedCountryCode = rule == "country_change" ? "DE" : null });
        await pending.SaveChangesAsync();
        Task<HttpResponseMessage>? firstScan = null; Task<HttpResponseMessage>? secondScan = null;
        HttpClient? laterAdmin = null; var firstBlocked = false; var secondBlocked = false; DateTimeOffset? during;
        try
        {
            fixture.Clock.Advance(TimeSpan.FromSeconds(10));
            firstScan = admin.PostAsync("/api/admin/operations/scan", null);
            firstBlocked = await WaitForBlockingOrCompletion(pending, firstScan, 1);
            fixture.Clock.Advance(TimeSpan.FromMinutes(minutes));
            laterAdmin = await fixture.Login();
            secondScan = laterAdmin.PostAsync("/api/admin/operations/scan", null);
            secondBlocked = await WaitForBlockingOrCompletion(pending, secondScan, 2);
            using var observedScope = fixture.Services.CreateScope();
            var observed = observedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            during = (await observed.RiskPolicies.AsNoTracking().SingleAsync(x => x.ApplicationId == app.Id)).ScannedThrough;
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        }
        finally
        {
            await transaction.CommitAsync();
            if (firstScan is not null) Assert.Equal(HttpStatusCode.OK, (await firstScan).StatusCode);
            if (secondScan is not null) Assert.Equal(HttpStatusCode.OK, (await secondScan).StatusCode);
            laterAdmin?.Dispose();
        }
        // With a one-minute rule these are scans at T+10s/T+70s, commit
        // at T+71s and a final scan at T+130s. No new fact rescues the burst.
        fixture.Clock.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
        using var finalAdmin = await fixture.Login();
        Assert.Equal(HttpStatusCode.OK, (await finalAdmin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var detected = Assert.Single((await finalAdmin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items);
        Assert.Equal(rule, detected.Rule); Assert.Equal(2, JsonDocument.Parse(detected.EvidenceJson).RootElement.GetProperty("count").GetInt32());
        Assert.True(firstBlocked); Assert.True(secondBlocked); Assert.True(during < factAt);
        Assert.True(detected.CreatedAt >= factAt.AddMinutes(minutes).AddSeconds(11));
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await finalAdmin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(detected.Id, Assert.Single((await finalAdmin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items).Id);
    }

    [Fact]
    public async Task ASecondScanDoesNotRecreateAnOlderActivationRiskWithLongCountryContext()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy", new UpdateRiskPolicy(true, 2, 10, 3, 60, 48))).StatusCode);
        var firstAt = fixture.Clock.GetUtcNow().AddHours(-2);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuthorizationEvents.AddRange(new AuthorizationEvent { ApplicationId = app.Id, LicenseId = issued.Licenses[0].Id, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = firstAt },
                new AuthorizationEvent { ApplicationId = app.Id, LicenseId = issued.Licenses[0].Id, Outcome = "ACTIVATION_SUCCEEDED", CreatedAt = firstAt.AddSeconds(1) });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        var original = Assert.Single((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items);
        fixture.Clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(original.Id, Assert.Single((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items).Id);
    }

    [Theory]
    [InlineData("activation")]
    [InlineData("release")]
    public async Task HttpFactWritersWaitForTheApplicationLockBeforeTakingTheirTimestamp(string kind)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var issued = await ApiFixture.Issue(admin, app, plan); var licenseId = issued.Licenses[0].Id;
        using var client = fixture.CreateClient(); Guid? bindingId = null;
        if (kind == "release")
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "cursor-http"))).StatusCode);
            bindingId = Assert.Single((await admin.GetFromJsonAsync<LicenseDetail>($"/api/admin/licenses/{licenseId}"))!.Bindings).Id;
        }
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/applications/{app.Id}/risk-policy",
            new UpdateRiskPolicy(true, kind == "activation" ? 1 : 10000, 1, 1, 1, 1))).StatusCode);
        using var blockingScope = fixture.Services.CreateScope(); var blocking = blockingScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await blocking.Database.BeginTransactionAsync();
        await blocking.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
        var cutoff = fixture.Clock.GetUtcNow();
        (await blocking.RiskPolicies.SingleAsync(x => x.ApplicationId == app.Id)).ScannedThrough = cutoff;
        await blocking.SaveChangesAsync();
        var writer = kind == "activation"
            ? client.PostAsJsonAsync("/api/client/licenses/activate", new ActivateLicense(app.Code, issued.Licenses[0].Key, "cursor-http"))
            : admin.PostAsync($"/api/admin/device-bindings/{bindingId}/release", null);
        bool waiting;
        try { waiting = await WaitForBlockingOrCompletion(blocking, writer, 1); fixture.Clock.Advance(TimeSpan.FromMinutes(2)); }
        finally { await transaction.CommitAsync(); }
        Assert.Equal(kind == "activation" ? HttpStatusCode.OK : HttpStatusCode.NoContent, (await writer).StatusCode);
        Assert.True(waiting);
        var writtenAt = kind == "activation"
            ? await blocking.AuthorizationEvents.Where(x => x.LicenseId == licenseId && x.Outcome == "ACTIVATION_SUCCEEDED").MaxAsync(x => x.CreatedAt)
            : (await blocking.Bindings.AsNoTracking().SingleAsync(x => x.Id == bindingId)).ReleasedAt!.Value;
        Assert.True(writtenAt > cutoff);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/operations/scan", null)).StatusCode);
        Assert.Equal(kind == "activation" ? "activation_burst" : "rebinding_burst",
            Assert.Single((await admin.GetFromJsonAsync<PagedResult<RiskEventDto>>($"/api/admin/risk-events?applicationId={app.Id}"))!.Items).Rule);
    }

    private static async Task<bool> WaitForBlockingOrCompletion(AppDbContext observer, Task<HttpResponseMessage> request, int minimumWaiting)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10))
        {
            if (request.IsCompleted) return false;
            await observer.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            var waiting = await observer.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_locks WHERE NOT granted AND pid IN (SELECT pid FROM pg_stat_activity WHERE datname = current_database())").SingleAsync();
            if (waiting >= minimumWaiting) return true;
            await Task.Delay(10);
        }
        return false;
    }
}
