using System.Net.Http.Json;
using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KeyValidation.IntegrationTests;

public sealed class RbacMigrationTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task RestrictedAndEmptyLegacyAccessBecomeExplicitRolesWithoutPrivilegeGain()
    {
        var (admin, app, _) = await fixture.Catalog(); using var cleanup = admin;
        var (restrictedId, restrictedName) = await RbacTestData.User(fixture);
        var (emptyId, emptyName) = await RbacTestData.User(fixture);
        var channel = Guid.NewGuid();
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261008135805_OperationsReliability");
            var historicalFailure = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"AuditLogs\" (\"Id\", \"ActorId\", \"Action\", \"TargetId\", \"Changes\", \"CreatedAt\", \"TraceId\") VALUES ({historicalFailure}, {restrictedId}, 'license.extend', {Guid.NewGuid()}, {"{\"success\":false,\"errorCode\":\"NOT_FOUND\"}"}, {fixture.Clock.GetUtcNow()}, 'historic-failure')");
            // Deliberately persisted over-ceiling grants model the old effective
            // intersection: a ReadOnly account could never issue or integrate.
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"AccessConfigured\" = true, \"AccessAllApplications\" = false, \"AccessApplicationIds\" = {new[] { app.Id }}, \"AccessPermissions\" = {new[] { "licenses.read", "licenses.issue", "integrations.manage" }}, \"AccessChannelId\" = {channel} WHERE \"Id\" = {restrictedId}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"AspNetUsers\" SET \"AccessConfigured\" = true, \"AccessAllApplications\" = false, \"AccessApplicationIds\" = {Array.Empty<Guid>()}, \"AccessPermissions\" = {Array.Empty<string>()} WHERE \"Id\" = {emptyId}");
            await migrator.MigrateAsync();
            Assert.False(db.Database.HasPendingModelChanges());
            var historical = await db.AuditLogs.AsNoTracking().SingleAsync(x => x.Id == historicalFailure);
            Assert.Equal("failed", historical.Result); Assert.Null(historical.SourceIp); Assert.Null(historical.ActorName);
            var names = await (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id where membership.UserId == restrictedId || membership.UserId == emptyId select role.Name).ToArrayAsync();
            Assert.Equal(2, names.Length); Assert.All(names, name => Assert.Contains("历史", name));
            var restricted = await db.Users.AsNoTracking().SingleAsync(x => x.Id == restrictedId);
            Assert.Empty(restricted.AccessPermissions); Assert.Equal(channel, restricted.AccessChannelId); Assert.Equal(new[] { app.Id }, restricted.AccessApplicationIds);
        }
        using var reader = await fixture.Login(restrictedName);
        var access = (await reader.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!;
        Assert.Equal(new[] { "licenses.read" }, access.Permissions); Assert.False(access.AllApplications);
        using var empty = await fixture.Login(emptyName);
        var denied = (await empty.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!;
        Assert.Empty(denied.Permissions); Assert.Empty(denied.ApplicationIds); Assert.False(denied.AllApplications);
        using var ordinary = await fixture.Login("operator");
        Assert.Equal(new[] { "audit.read", "customers.manage", "licenses.issue", "licenses.manage", "licenses.read", "orders.manage", "reports.read", "risk.review" }, (await ordinary.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!.Permissions.Order());

        // Runtime adapters use the same generated name with a random role ID.
        var (runtimeId, runtimeName) = await RbacTestData.User(fixture);
        (await admin.PutAsJsonAsync($"/api/admin/accounts/{runtimeId}/access", new
        {
            permissions = new[] { "licenses.read" }, allApplications = false, applicationIds = new[] { app.Id }
        })).EnsureSuccessStatusCode();

        // Roll back after conversion has created deterministic Identity roles.
        // The older authorization model denies these custom memberships, so the
        // next upgrade must keep that conservative result and preserve scope.
        using (var scope = fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var migrator = db.GetService<IMigrator>();
            var converted = await db.RoleDefinitions.AsNoTracking().Where(x => x.LegacyUserId == restrictedId || x.LegacyUserId == emptyId || x.LegacyUserId == runtimeId)
                .ToDictionaryAsync(x => x.LegacyUserId!, x => x.RoleId);
            Assert.Equal(3, converted.Count);
            await migrator.MigrateAsync("20261008135805_OperationsReliability");
            var downgraded = await db.Users.AsNoTracking().SingleAsync(x => x.Id == restrictedId);
            Assert.True(downgraded.AccessConfigured); Assert.Equal(new[] { "licenses.read" }, downgraded.AccessPermissions);
            Assert.Empty((await db.Users.AsNoTracking().SingleAsync(x => x.Id == emptyId)).AccessPermissions);
            await migrator.MigrateAsync();

            Assert.False(db.Database.HasPendingModelChanges());
            foreach (var (userId, roleId) in converted)
            {
                var definition = await db.RoleDefinitions.AsNoTracking().SingleAsync(x => x.LegacyUserId == userId);
                Assert.Equal(roleId, definition.RoleId); Assert.False(definition.IsBuiltIn);
                Assert.Equal(new[] { roleId }, await db.UserRoles.Where(x => x.UserId == userId).Select(x => x.RoleId).ToArrayAsync());
                Assert.Empty(await db.RolePermissions.Where(x => x.RoleId == roleId).ToArrayAsync());
                Assert.Empty((await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId)).AccessPermissions);
            }
            var reapplied = await db.Users.AsNoTracking().SingleAsync(x => x.Id == restrictedId);
            Assert.Equal(channel, reapplied.AccessChannelId); Assert.Equal(new[] { app.Id }, reapplied.AccessApplicationIds); Assert.False(reapplied.AccessAllApplications);
            var emptyScope = await db.Users.AsNoTracking().SingleAsync(x => x.Id == emptyId);
            Assert.Null(emptyScope.AccessChannelId); Assert.Empty(emptyScope.AccessApplicationIds); Assert.False(emptyScope.AccessAllApplications);
            var runtimeScope = await db.Users.AsNoTracking().SingleAsync(x => x.Id == runtimeId);
            Assert.Null(runtimeScope.AccessChannelId); Assert.Equal(new[] { app.Id }, runtimeScope.AccessApplicationIds); Assert.False(runtimeScope.AccessAllApplications);
        }
        using var reappliedReader = await fixture.Login(restrictedName);
        Assert.Empty((await reappliedReader.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!.Permissions);
        using var reappliedEmpty = await fixture.Login(emptyName);
        Assert.Empty((await reappliedEmpty.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!.Permissions);
        using var reappliedRuntime = await fixture.Login(runtimeName);
        Assert.Empty((await reappliedRuntime.GetFromJsonAsync<AccountAccessDto>("/api/admin/auth/access"))!.Permissions);
    }
}
