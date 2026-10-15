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

public sealed class CommerceAuditTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CustomerAndPortalLabelsFollowCurrentNamesWithoutRetainingPersonalSnapshots()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        const string original = "Original audit customer", current = "Current audit customer";
        var (customer, order, request) = await Targets(admin, app.Id, plan.Id, null, original);
        var actor = (await admin.GetFromJsonAsync<UserDto>("/api/admin/auth/me"))!;
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Generic ingestion must also reject an identifying target snapshot.
            db.AuditLogs.Add(new() { ActorId = actor.Id, Action = "customer.update", TargetId = customer, TargetName = original, ApplicationId = app.Id, CreatedAt = fixture.Clock.GetUtcNow(), TraceId = "customer-label-ingestion" });
            db.AuditLogs.Add(new() { ActorId = actor.Id, Action = "portal-request.review", TargetId = request, ApplicationId = app.Id, CreatedAt = fixture.Clock.GetUtcNow(), TraceId = "portal-label-ingestion" });
            await db.SaveChangesAsync();
        }
        await Labels(admin, customer, original);
        await Labels(admin, request, original + " · 续期申请");
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/customers/{customer}", new { name = current, email = "current-private@example.test", phone = "current-private-phone", note = "current-private-note" })).StatusCode);
        await Labels(admin, customer, current);
        await Labels(admin, request, current + " · 续期申请");
        await NoPersonalSnapshots();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"/api/admin/customers/{customer}/anonymize", null)).StatusCode);
        await Labels(admin, customer, "客户 " + customer.ToString("N")[..8]);
        await Labels(admin, request, "自助申请 " + request.ToString("N")[..8]);
        await NoPersonalSnapshots();
        foreach (var target in new[] { customer, order, request })
        {
            var visible = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={target}");
            foreach (var value in PrivateValues()) Assert.DoesNotContain(value, visible);
        }

        string[] PrivateValues() => [original, current, "private@example.test", "+86-private", "private-note", "current-private@example.test", "current-private-phone", "current-private-note", "private portal request"];
        async Task NoPersonalSnapshots()
        {
            using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var targets = new[] { customer, order, request };
            var snapshots = JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().Where(x => targets.Contains(x.TargetId)).Select(x => new { x.TargetName, x.Changes }).ToArrayAsync());
            foreach (var value in PrivateValues()) Assert.DoesNotContain(value, snapshots);
        }
    }

    [Fact]
    public async Task OrderLabelsUseTheImmutablePlanSnapshotTypeAndQuantity()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var customer = CommerceTestData.Id(await CommerceTestData.Customer(admin, app.Id));
        var issue = CommerceTestData.Id(await CommerceTestData.Order(admin, app.Id, plan.Id, customer, 2));
        var done = await CommerceTestData.Fulfill(admin, issue);
        var renewal = CommerceTestData.Id(await CommerceTestData.Order(admin, app.Id, plan.Id, customer, type: "renewal", license: done.GetProperty("licenseIds")[0].GetGuid()));
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/plans/{plan.Id}", new UpdatePlan("Plan renamed after order creation", plan.DurationSeconds, plan.MaxDevices, true))).StatusCode);
        await Labels(admin, issue, $"{plan.Name} · 发卡 × 2");
        await Labels(admin, renewal, $"{plan.Name} · 续期 × 1");
    }

    [Fact]
    public async Task CommerceLabelResolutionNeverUsesNamesOutsideApplicationOrChannelScope()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (otherAdmin, otherApp, otherPlan) = await fixture.Catalog(); using var otherCleanup = otherAdmin;
        var (ownChannel, ownName, ownUser) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var (otherChannel, _, _) = await CommerceTestData.Channel(fixture, admin, app.Id);
        var ownChannelId = CommerceTestData.Id(ownChannel);
        await CommerceTestData.Price(admin, ownChannelId, app.Id, plan.Id);
        await CommerceTestData.Price(admin, CommerceTestData.Id(otherChannel), app.Id, plan.Id);
        var own = await Targets(admin, app.Id, plan.Id, ownChannelId, "Visible channel customer");
        var outsideChannel = await Targets(admin, app.Id, plan.Id, CommerceTestData.Id(otherChannel), "Hidden channel customer");
        var outsideApplication = await Targets(admin, otherApp.Id, otherPlan.Id, null, "Hidden application customer");
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var targets in new[] { outsideChannel, outsideApplication })
            foreach (var (type, id) in new[] { ("customer", targets.Customer), ("order", targets.Order), ("portal-request", targets.Request) })
                db.AuditLogs.Add(new() { ActorId = ownUser, Action = type + ".create", TargetId = id, ApplicationId = app.Id, ChannelId = ownChannelId, CreatedAt = fixture.Clock.GetUtcNow(), TraceId = "mismatched-commerce-owner" });
            await db.SaveChangesAsync();
        }
        using var channelReader = await fixture.Login(ownName);
        await Labels(channelReader, own.Customer, "Visible channel customer");
        foreach (var targets in new[] { outsideChannel, outsideApplication })
        foreach (var target in new[] { targets.Customer, targets.Order, targets.Request })
        {
            var rows = (await channelReader.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}")).GetProperty("items");
            Assert.Equal(JsonValueKind.Null, Assert.Single(rows.EnumerateArray()).GetProperty("targetName").ValueKind);
            Assert.DoesNotContain("Hidden", rows.GetRawText());
        }
        var (readerId, readerName) = await RbacTestData.User(fixture);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{readerId}/access", new { permissions = new[] { "audit.read" }, allApplications = false, applicationIds = new[] { app.Id } })).StatusCode);
        using var appReader = await fixture.Login(readerName);
        await Labels(appReader, outsideChannel.Customer, "Hidden channel customer");
        foreach (var target in new[] { outsideApplication.Customer, outsideApplication.Order, outsideApplication.Request })
        {
            var rows = (await appReader.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}")).GetProperty("items");
            Assert.Equal(JsonValueKind.Null, Assert.Single(rows.EnumerateArray()).GetProperty("targetName").ValueKind);
            Assert.DoesNotContain("Hidden application customer", rows.GetRawText());
        }
    }

    private async Task<(Guid Customer, Guid Order, Guid Request)> Targets(HttpClient admin, Guid app, Guid plan, Guid? channel, string name)
    {
        var customer = CommerceTestData.Id(await CommerceTestData.Customer(admin, app, channel, name));
        var order = CommerceTestData.Id(await CommerceTestData.Order(admin, app, plan, customer));
        var done = await CommerceTestData.Fulfill(admin, order);
        var (portal, _) = await CommerceTestData.Portal(fixture, admin, customer); using var portalCleanup = portal;
        var request = CommerceTestData.Id(await CommerceTestData.Json(await portal.PostAsJsonAsync("/api/portal/requests", new { requestId = Guid.NewGuid(), type = "renewal", licenseId = done.GetProperty("licenseIds")[0].GetGuid(), note = "private portal request" }), HttpStatusCode.Created));
        return (customer, order, request);
    }

    private static async Task Labels(HttpClient reader, Guid target, string expected)
    {
        var rows = (await reader.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={target}&pageSize=100")).GetProperty("items").EnumerateArray().ToArray();
        Assert.NotEmpty(rows);
        Assert.All(rows, row => Assert.Equal(expected, row.GetProperty("targetName").GetString()));
    }
}
