using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class RbacLastAdminTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task LegacyRoleChangeCannotRemoveTheLastAdminFromAMixedAssignment()
    {
        using var originalAdmin = await fixture.Login();
        var original = (await originalAdmin.GetFromJsonAsync<UserDto>("/api/admin/auth/me"))!;
        var roles = (await originalAdmin.GetFromJsonAsync<PagedResult<RbacTestRole>>("/api/admin/roles"))!.Items;
        var builtInAdmin = Assert.Single(roles, x => x.Name == "Admin" && x.IsBuiltIn);
        var reader = Assert.Single(roles, x => x.Name == "ReadOnly");
        var (id, name) = await RbacTestData.User(fixture);
        Assert.Equal(HttpStatusCode.OK, (await originalAdmin.PutAsJsonAsync($"/api/admin/accounts/{id}/access", new
        {
            permissions = new[] { "licenses.read" }, allApplications = true, applicationIds = Array.Empty<Guid>()
        })).StatusCode);

        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var legacyRoleId = (await db.RoleDefinitions.AsNoTracking().SingleAsync(x => x.LegacyUserId == id)).RoleId;
        await RbacTestData.Assign(originalAdmin, id, [legacyRoleId, builtInAdmin.Id]);
        using var actor = await fixture.Login(name);
        await RbacTestData.Assign(actor, original.Id, [reader.Id]);

        var enabledAdmins = await (from membership in db.UserRoles
                                   join user in db.Users on membership.UserId equals user.Id
                                   where membership.RoleId == builtInAdmin.Id && user.DisabledAt == null
                                   select user.Id).ToArrayAsync();
        Assert.Equal(new[] { id }, enabledAdmins);
        var memberships = await db.UserRoles.Where(x => x.UserId == id).Select(x => x.RoleId).Order().ToArrayAsync();
        var grants = await db.RolePermissions.Where(x => x.RoleId == legacyRoleId).Select(x => x.Permission).Order().ToArrayAsync();
        var stamp = await db.Users.Where(x => x.Id == id).Select(x => x.SecurityStamp).SingleAsync();
        var audits = await db.AuditLogs.CountAsync();

        var response = await actor.PatchAsJsonAsync($"/api/admin/accounts/{id}/role", new { role = "Admin" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("LAST_ADMIN_REQUIRED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(memberships, await db.UserRoles.Where(x => x.UserId == id).Select(x => x.RoleId).Order().ToArrayAsync());
        Assert.Equal(grants, await db.RolePermissions.Where(x => x.RoleId == legacyRoleId).Select(x => x.Permission).Order().ToArrayAsync());
        Assert.Equal(stamp, await db.Users.Where(x => x.Id == id).Select(x => x.SecurityStamp).SingleAsync());
        Assert.Equal(audits, await db.AuditLogs.CountAsync());
        Assert.Equal(HttpStatusCode.OK, (await actor.GetAsync("/api/admin/auth/me")).StatusCode);
    }
}
