using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Api;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class AccountTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task CreateAndSearchAccountsIncludeCliAccountsAndNeverExposeCredentials()
    {
        using var admin = await fixture.Login();
        var name = "account-" + Guid.NewGuid().ToString("N");
        var response = await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name, password = fixture.Password, role = "Operator" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString()!;
        Assert.Equal(name, created.GetProperty("userName").GetString());
        Assert.Equal("Operator", Assert.Single(created.GetProperty("roles").EnumerateArray()).GetString());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("disabledAt").ValueKind);
        Assert.Equal(fixture.Clock.GetUtcNow(), created.GetProperty("createdAt").GetDateTimeOffset());
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts?search={name.ToUpperInvariant()}&page=1&pageSize=1");
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        Assert.Equal(id, Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("id").GetString());
        var cliAccount = await admin.GetFromJsonAsync<JsonElement>("/api/admin/accounts?search=readonly");
        Assert.Equal("readonly", Assert.Single(cliAccount.GetProperty("items").EnumerateArray()).GetProperty("userName").GetString());
        foreach (var path in new[] { "/api/admin/accounts", $"/api/admin/audit-logs?targetId={id}" })
        {
            var body = await admin.GetStringAsync(path);
            Assert.DoesNotContain(fixture.Password, body);
            Assert.DoesNotContain("passwordHash", body);
            Assert.DoesNotContain("securityStamp", body);
        }
        var audit = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/audit-logs?targetId={id}");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), x => x.GetProperty("action").GetString() == "account.create");
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name.ToUpperInvariant(), password = fixture.Password, role = "ReadOnly" })).StatusCode);
    }

    [Theory]
    [InlineData("weak", "Admin")]
    [InlineData("NoSpecialCharacter123", "Admin")]
    [InlineData("NOLOWERCASE123!", "Admin")]
    [InlineData("nouppercase123!", "Admin")]
    [InlineData("NoDigitsAllowed!", "Admin")]
    [InlineData("StrongPassword123!", "Owner")]
    public async Task CreationRejectsWeakPasswordsAndUnknownRoles(string password, string role)
    {
        using var admin = await fixture.Login();
        var name = "invalid-" + Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name, password, role })).StatusCode);
        var list = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/accounts?search={name}");
        Assert.Equal(0, list.GetProperty("total").GetInt32());
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("readonly")]
    public async Task EveryAccountEndpointRequiresAdmin(string userName)
    {
        using var client = await fixture.Login(userName);
        var id = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/accounts", new { userName = "forbidden", password = fixture.Password, role = "Admin" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Admin" })).StatusCode);
        foreach (var action in new[] { "disable", "enable" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/admin/accounts/{id}/{action}", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync($"/api/admin/accounts/{id}/reset-password", new { password = fixture.Password })).StatusCode);
    }

    [Fact]
    public async Task AccountWritesRequireCsrfAndListValidatesPaging()
    {
        using var admin = await fixture.Login();
        admin.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = "csrf", password = fixture.Password, role = "Admin" })).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=1000001" })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/api/admin/accounts?" + query)).StatusCode);
    }

    [Fact]
    public async Task DisableBlocksLoginAndRevokesCookieEvenAfterEnable()
    {
        using var admin = await fixture.Login();
        var (id, name) = await Create(admin, "Operator");
        using var oldSession = await LoginCreatedAccount(name);
        using var untouchedOldSession = await LoginCreatedAccount(name);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/accounts/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/admin/auth/me")).StatusCode);
        using var anonymous = fixture.CreateClient();
        await ApiFixture.Csrf(anonymous);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, AccountPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"/api/admin/accounts/{id}/enable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/admin/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await untouchedOldSession.GetAsync("/api/admin/auth/me")).StatusCode);
        using var freshSession = await LoginCreatedAccount(name);
        Assert.Equal(HttpStatusCode.OK, (await freshSession.GetAsync("/api/admin/auth/me")).StatusCode);
    }

    [Fact]
    public async Task RoleReplacementIsSingleAndRevokesPriorAuthorityImmediately()
    {
        using var admin = await fixture.Login();
        var (id, name) = await Create(admin, "Admin");
        using var oldSession = await LoginCreatedAccount(name);
        var response = await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "ReadOnly" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ReadOnly", Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("roles").EnumerateArray()).GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/admin/accounts")).StatusCode);
        using var freshSession = await LoginCreatedAccount(name);
        Assert.Equal(HttpStatusCode.Forbidden, (await freshSession.GetAsync("/api/admin/accounts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await freshSession.GetAsync("/api/admin/licenses")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Owner" })).StatusCode);
    }

    [Fact]
    public async Task PasswordResetValidatesPasswordClearsLockoutAndRevokesOldCookies()
    {
        using var admin = await fixture.Login();
        var (id, name) = await Create(admin, "Operator");
        using var oldSession = await LoginCreatedAccount(name);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/api/admin/accounts/{id}/reset-password", new { password = "weak" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await oldSession.GetAsync("/api/admin/auth/me")).StatusCode);
        using (var scope = fixture.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
            var user = (await users.FindByIdAsync(id))!;
            Assert.True((await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddMinutes(15))).Succeeded);
        }
        var password = fixture.Password + "New1!";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"/api/admin/accounts/{id}/reset-password", new { password })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldSession.GetAsync("/api/admin/auth/me")).StatusCode);
        using var fresh = fixture.CreateClient();
        await ApiFixture.Csrf(fresh);
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, AccountPassword))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fresh.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, password))).StatusCode);
        var audit = await admin.GetStringAsync($"/api/admin/audit-logs?targetId={id}");
        Assert.Contains("account.reset-password", audit);
        Assert.DoesNotContain(password, audit);
    }

    [Fact]
    public async Task AdministratorCannotDisableOrDemoteSelf()
    {
        using var admin = await fixture.Login();
        var me = await admin.GetFromJsonAsync<JsonElement>("/api/admin/auth/me");
        var id = me.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/api/admin/accounts/{id}/disable", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Operator" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/admin/accounts")).StatusCode);
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("demote")]
    public async Task ConcurrentAdministratorChangesPreserveOneEnabledAdministrator(string action)
    {
        using var admin = await fixture.Login();
        var (firstId, firstName) = await Create(admin, "Admin");
        var (secondId, secondName) = await Create(admin, "Admin");
        using var first = await LoginCreatedAccount(firstName);
        using var second = await LoginCreatedAccount(secondName);
        // Leave just these two enabled administrators, then restore the fixture account.
        using var scope = fixture.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AdminUser>>();
        var root = (await users.FindByNameAsync("admin"))!;
        Assert.True((await users.RemoveFromRoleAsync(root, "Admin")).Succeeded);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var priorAdmins = await users.GetUsersInRoleAsync("Admin");
        foreach (var other in priorAdmins.Where(x => x.Id != firstId && x.Id != secondId))
            Assert.True((await users.RemoveFromRoleAsync(other, "Admin")).Succeeded);
        try
        {
            var responses = await Task.WhenAll(Change(first, secondId), Change(second, firstId));
            Assert.Single(responses, x => x.IsSuccessStatusCode);
            Assert.Single(responses, x => x.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.Conflict);
            var enabled = await (from user in db.Users.AsNoTracking()
                                 join membership in db.UserRoles on user.Id equals membership.UserId
                                 join role in db.Roles on membership.RoleId equals role.Id
                                 where role.Name == "Admin" && EF.Property<DateTimeOffset?>(user, "DisabledAt") == null
                                 select user.Id).CountAsync();
            Assert.Equal(1, enabled);
        }
        finally
        {
            Assert.True((await users.AddToRoleAsync(root, "Admin")).Succeeded);
            foreach (var other in priorAdmins.Where(x => x.Id != firstId && x.Id != secondId))
                Assert.True((await users.AddToRoleAsync(other, "Admin")).Succeeded);
        }

        Task<HttpResponseMessage> Change(HttpClient client, string target) => action == "disable"
            ? client.PostAsync($"/api/admin/accounts/{target}/disable", null)
            : client.PatchAsJsonAsync($"/api/admin/accounts/{target}/role", new { role = "ReadOnly" });
    }

    [Fact]
    public async Task QueuedMutationRechecksAdministratorAuthorityAfterWaitingForTheLock()
    {
        using var admin = await fixture.Login();
        var (firstId, firstName) = await Create(admin, "Admin");
        var (secondId, secondName) = await Create(admin, "Admin");
        using var first = await LoginCreatedAccount(firstName);
        using var second = await LoginCreatedAccount(secondName);
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await db.Database.BeginTransactionAsync();
        // Both requests must authenticate before either can change account state.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
        var firstRequest = first.PostAsync($"/api/admin/accounts/{secondId}/disable", null);
        var secondRequest = second.PostAsync($"/api/admin/accounts/{firstId}/disable", null);
        var waiting = 0;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (waiting < 2 && DateTime.UtcNow < deadline)
        {
            waiting = await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())").SingleAsync();
            if (waiting < 2) await Task.Delay(20);
        }
        await tx.CommitAsync();
        var responses = await Task.WhenAll(firstRequest, secondRequest);
        Assert.Equal(2, waiting);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        // The untouched fixture administrator means last-admin protection cannot mask stale authority.
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Forbidden);
    }

    private async Task<(string Id, string Name)> Create(HttpClient admin, string role)
    {
        var name = "account-" + Guid.NewGuid().ToString("N");
        var response = await admin.PostAsJsonAsync("/api/admin/accounts", new { userName = name, password = fixture.Password, role });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(account.GetProperty("mustChangePassword").GetBoolean());
        // Account lifecycle scenarios begin after the new, required first password change.
        using var firstLogin = await fixture.Login(name);
        var changed = await firstLogin.PostAsJsonAsync("/api/admin/security/password", new { currentPassword = fixture.Password, newPassword = AccountPassword });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        return (account.GetProperty("id").GetString()!, name);
    }

    private string AccountPassword => fixture.Password + "Changed1!";
    private async Task<HttpClient> LoginCreatedAccount(string name)
    {
        var client = fixture.CreateClient();
        await ApiFixture.Csrf(client);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/admin/auth/login", new LoginRequest(name, AccountPassword))).StatusCode);
        await ApiFixture.Csrf(client);
        return client;
    }
}
