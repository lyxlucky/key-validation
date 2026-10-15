using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class RbacAdministrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task TwoCustomRolesGrantTheirUnionWithoutANameBasedCeiling()
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var read = await RbacTestData.Role(admin, ["licenses.read"]);
        var issue = await RbacTestData.Role(admin, ["licenses.issue", "integrations.manage"]);
        var (id, name) = await RbacTestData.User(fixture);
        using var old = await fixture.Login(name);
        var assigned = await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/roles", new { roleIds = new[] { read.Id, issue.Id } });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var account = await assigned.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { read.Id, issue.Id }.Order(), account.GetProperty("roleIds").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.GetAsync("/api/admin/auth/me")).StatusCode);
        using var actor = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/admin/auth/me")).StatusCode);
        var access = (await actor.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!;
        Assert.Equal(new[] { "integrations.manage", "licenses.issue", "licenses.read" }, access.Permissions.Order());
        Assert.Equal(HttpStatusCode.Created, (await actor.PostAsJsonAsync("/api/admin/license-batches", new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync($"/api/admin/webhook-subscriptions?applicationId={app.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/reports/operations")).StatusCode);
    }

    [Fact]
    public async Task AnEmptyCustomRoleCanUseSelfServiceButNoBusinessEndpoint()
    {
        using var admin = await fixture.Login();
        var role = await RbacTestData.Role(admin, []);
        var name = "custom-" + Guid.NewGuid().ToString("N");
        var response = await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name, password = fixture.Password, roleIds = new[] { role.Id } });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var actor = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/admin/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await actor.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = fixture.Password + "New1!" })).StatusCode);
        await ApiFixture.Csrf(actor);
        Assert.Empty((await actor.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!.Permissions);
        foreach (var route in new[] { "licenses", "roles", "permissions", "accounts", "audit-logs", "login-logs" })
            Assert.Equal(HttpStatusCode.Forbidden, (await actor.GetAsync("/api/admin/" + route)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await actor.PostAsync("/api/admin/auth/logout", null)).StatusCode);
    }

    [Fact]
    public async Task PlatformManagementComesFromTheGrantAndScopeStillFiltersResources()
    {
        var (admin, visible, plan) = await fixture.Catalog(); using var cleanup = admin;
        var (other, hidden, otherPlan) = await fixture.Catalog(); using var otherCleanup = other;
        var hiddenBatch = await ApiFixture.Issue(admin, hidden, otherPlan);
        await ApiFixture.Issue(admin, visible, plan);
        var role = await RbacTestData.Role(admin, ["platform.manage", "licenses.read", "audit.read"]);
        var (id, name) = await RbacTestData.User(fixture);
        await RbacTestData.Assign(admin, id, [role.Id]);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { allApplications = false, applicationIds = new[] { visible.Id } })).StatusCode);
        using var actor = await fixture.Login(name);
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/admin/roles")).StatusCode);
        var licenses = (await actor.GetFromJsonAsync<PagedResult<LicenseDto>>("/api/admin/licenses?pageSize=1"))!;
        Assert.Equal(1, licenses.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.GetAsync($"/api/admin/licenses/{hiddenBatch.Licenses[0].Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await actor.PostAsync($"/api/admin/applications/{hidden.Id}/disable", null)).StatusCode);
        Assert.Equal(0, (await actor.GetFromJsonAsync<PagedResult<AuditDto>>($"/api/admin/audit-logs?targetId={hidden.Id}"))!.Total);
    }

    [Fact]
    public async Task PermissionEditsRevokeAllMembersAndRoleDeletionRequiresNoMembers()
    {
        using var admin = await fixture.Login();
        var role = await RbacTestData.Role(admin, ["licenses.read"]);
        var (firstId, firstName) = await RbacTestData.User(fixture);
        var (secondId, secondName) = await RbacTestData.User(fixture);
        await RbacTestData.Assign(admin, firstId, [role.Id]); await RbacTestData.Assign(admin, secondId, [role.Id]);
        using var first = await fixture.Login(firstName); using var second = await fixture.Login(secondName);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/roles/{role.Id}")).StatusCode);
        Assert.Equal(2, (await admin.GetFromJsonAsync<RbacTestRole>($"/api/admin/roles/{role.Id}"))!.UserCount);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/roles/{role.Id}", new { role.Name, description = "Permissions removed", permissions = Array.Empty<string>() })).StatusCode);
        foreach (var client in new[] { first, second }) Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/licenses")).StatusCode);
        using var fresh = await fixture.Login(firstName);
        Assert.Equal(HttpStatusCode.Forbidden, (await fresh.GetAsync("/api/admin/licenses")).StatusCode);
        var roles = (await admin.GetFromJsonAsync<PagedResult<RbacTestRole>>("/api/admin/roles?search=ReadOnly"))!;
        var reader = Assert.Single(roles.Items, x => x.Name == "ReadOnly");
        await RbacTestData.Assign(admin, firstId, [reader.Id]); await RbacTestData.Assign(admin, secondId, [reader.Id]);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/roles/{role.Id}")).StatusCode);
        var audit = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={role.Id}&action=role.delete");
        Assert.Equal(role.Name, Assert.Single(audit.GetProperty("items").EnumerateArray()).GetProperty("targetName").GetString());
    }

    [Fact]
    public async Task BuiltInAdminCannotBeEditedDeletedOrReplacedWhenItIsTheLastOne()
    {
        using var admin = await fixture.Login();
        var roles = (await admin.GetFromJsonAsync<PagedResult<RbacTestRole>>("/api/admin/roles"))!.Items;
        var builtIn = Assert.Single(roles, x => x.Name == "Admin"); Assert.True(builtIn.IsBuiltIn);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/roles/{builtIn.Id}", new { name = "Renamed", permissions = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/roles/{builtIn.Id}")).StatusCode);
        var platform = await RbacTestData.Role(admin, ["platform.manage"]);
        var (managerId, managerName) = await RbacTestData.User(fixture); await RbacTestData.Assign(admin, managerId, [platform.Id]);
        using var manager = await fixture.Login(managerName);
        var root = (await admin.GetFromJsonAsync<UserDto>("/api/admin/auth/me"))!;
        var denied = await manager.PatchAsJsonAsync($"/api/admin/accounts/{root.Id}/roles", new { roleIds = new[] { platform.Id } });
        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        Assert.Equal("LAST_ADMIN_REQUIRED", (await denied.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await manager.PostAsync($"/api/admin/accounts/{root.Id}/disable", null)).StatusCode);
    }

    [Fact]
    public async Task AChannelAccountCannotGainPlatformThroughAssignmentOrSharedRoleEdit()
    {
        using var admin = await fixture.Login();
        var platform = await RbacTestData.Role(admin, ["platform.manage"]);
        var ordinary = await RbacTestData.Role(admin, ["licenses.read"]);
        var (id, _) = await RbacTestData.User(fixture); await RbacTestData.Assign(admin, id, [ordinary.Id]);
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(x => x.Id == id); user.AccessChannelId = Guid.NewGuid(); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/roles", new { roleIds = new[] { platform.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/roles/{ordinary.Id}", new { ordinary.Name, permissions = new[] { "licenses.read", "platform.manage" } })).StatusCode);
        Assert.Equal(new[] { "licenses.read" }, (await admin.GetFromJsonAsync<RbacTestRole>($"/api/admin/roles/{ordinary.Id}"))!.Permissions);
    }

    [Fact]
    public async Task PermissionCatalogIsControlledAndRoleInputsAreValidated()
    {
        using var admin = await fixture.Login();
        var catalog = await admin.GetFromJsonAsync<JsonElement>("/api/admin/permissions");
        Assert.Equal(10, catalog.GetArrayLength());
        Assert.Contains(catalog.EnumerateArray(), x => x.GetProperty("code").GetString() == "platform.manage");
        Assert.All(catalog.EnumerateArray(), x => { Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("label").GetString())); Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("group").GetString())); Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("description").GetString())); });
        foreach (var body in new object[] { new { name = " ", permissions = Array.Empty<string>() }, new { name = new string('x', 65), permissions = Array.Empty<string>() }, new { name = "unknown", permissions = new[] { "root.everything" } }, new { name = "too-long", description = new string('x', 501), permissions = Array.Empty<string>() } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/roles", body)).StatusCode);
        var role = await RbacTestData.Role(admin, []);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/roles", new { name = role.Name.ToUpperInvariant(), permissions = Array.Empty<string>() })).StatusCode);
        var (id, _) = await RbacTestData.User(fixture);
        foreach (var roleIds in new[] { Array.Empty<string>(), new[] { role.Id, role.Id }, new[] { Guid.NewGuid().ToString() } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/roles", new { roleIds })).StatusCode);
        var search = (await admin.GetFromJsonAsync<PagedResult<RbacTestRole>>($"/api/admin/roles?search={role.Name.ToUpperInvariant()}&pageSize=1"))!;
        Assert.Equal(1, search.Total); Assert.Equal(role.Id, Assert.Single(search.Items).Id);
    }

    [Fact]
    public async Task AnIntentionallyEmptyBuiltInRoleStaysEmptyAfterApplicationRestart()
    {
        using var admin = await fixture.Login();
        var role = Assert.Single((await admin.GetFromJsonAsync<PagedResult<RbacTestRole>>("/api/admin/roles?search=Operator"))!.Items, x => x.Name == "Operator");
        try
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/roles/{role.Id}", new { role.Name, role.Description, permissions = Array.Empty<string>() })).StatusCode);
            await using var restarted = fixture.WithWebHostBuilder(_ => { });
            using var client = restarted.CreateClient(); await ApiFixture.Csrf(client);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest("admin", fixture.Password))).StatusCode);
            Assert.Empty((await client.GetFromJsonAsync<RbacTestRole>($"/api/admin/roles/{role.Id}"))!.Permissions);
        }
        finally { Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/roles/{role.Id}", new { role.Name, role.Description, role.Permissions })).StatusCode); }
    }

    [Fact]
    public async Task LegacyPermissionAdapterCreatesAnExplicitRoleAndOldRoleChangesCannotBroadenIt()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var (id, name) = await RbacTestData.User(fixture);
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new { permissions = new[] { "licenses.read" }, allApplications = false, applicationIds = new[] { app.Id } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Operator" })).StatusCode);
        using var actor = await fixture.Login(name);
        var access = (await actor.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!;
        Assert.Equal(new[] { "licenses.read" }, access.Permissions); Assert.False(access.AllApplications); Assert.Equal(new[] { app.Id }, access.ApplicationIds);
        var account = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts?search={name}")).GetProperty("items").EnumerateArray());
        var role = (await admin.GetFromJsonAsync<RbacTestRole>($"/api/admin/roles/{Assert.Single(account.GetProperty("roleIds").EnumerateArray()).GetString()}"))!;
        Assert.Equal(new[] { "licenses.read" }, role.Permissions); Assert.False(role.IsBuiltIn); Assert.Contains("历史", role.Name);
        using var scope = fixture.Services.CreateScope();
        Assert.Empty((await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.SingleAsync(x => x.Id == id)).AccessPermissions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoleRevocationStopsAQueuedBusinessWriteIncludingANoChangeRequest(bool noChange)
    {
        var (admin, app, plan) = await fixture.Catalog(); using var cleanup = admin;
        var batch = await ApiFixture.Issue(admin, app, plan);
        var role = await RbacTestData.Role(admin, ["licenses.issue", "licenses.manage"]);
        var (id, name) = await RbacTestData.User(fixture); await RbacTestData.Assign(admin, id, [role.Id]);
        using var actor = await fixture.Login(name);
        using var scope = fixture.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {app.Id} FOR UPDATE");
        var request = new IssueBatch(app.Id, plan.Id, 1, null, Guid.NewGuid());
        var pending = noChange ? actor.PostAsync($"/api/admin/licenses/{batch.Licenses[0].Id}/enable", null) : actor.PostAsJsonAsync("/api/admin/license-batches", request);
        var waiting = await RbacTestData.WaitForBlockedRequest(db);
        try { if (waiting) Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/roles/{role.Id}", new { role.Name, permissions = Array.Empty<string>() })).StatusCode); }
        finally { await tx.CommitAsync(); }
        var response = await pending;
        Assert.True(waiting); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.False(await db.Batches.AnyAsync(x => x.RequestId == request.RequestId));
    }
}

internal sealed record RbacTestRole(string Id, string Name, string? Description, bool IsBuiltIn, string[] Permissions, int UserCount);

internal static class RbacTestData
{
    internal static async Task<RbacTestRole> Role(HttpClient admin, string[] permissions)
    {
        var response = await admin.PostAsJsonAsync("/api/admin/roles", new { name = "role-" + Guid.NewGuid().ToString("N"), description = "Test business role", permissions });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<RbacTestRole>())!;
    }
    internal static async Task<(string Id, string Name)> User(ApiFixture fixture)
    {
        using var scope = fixture.Services.CreateScope(); var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var user = new AdminUser { UserName = "rbac-" + Guid.NewGuid().ToString("N") };
        Assert.True((await users.CreateAsync(user, fixture.Password)).Succeeded); Assert.True((await users.AddToRoleAsync(user, "ReadOnly")).Succeeded);
        return (user.Id, user.UserName);
    }
    internal static async Task Assign(HttpClient admin, string id, string[] roleIds) => Assert.Equal(HttpStatusCode.OK, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/roles", new { roleIds })).StatusCode);
    internal static async Task<bool> WaitForBlockedRequest(AppDbContext db)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await db.Database.ExecuteSqlRawAsync("SELECT pg_stat_clear_snapshot()");
            if (await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_locks WHERE NOT granted AND pid IN (SELECT pid FROM pg_stat_activity WHERE datname = current_database())").SingleAsync() > 0) return true;
            await Task.Delay(20);
        }
        return false;
    }
}
