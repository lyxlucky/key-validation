using System.Security.Claims;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
public sealed partial class AdminAccountService
{
    public async Task<PagedResult<RoleDto>> ListRoles(int page, int pageSize, string? search)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || search?.Length > 128)
            throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.Roles.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = roleManager.NormalizeKey(search.Trim())!;
            query = query.Where(x => x.NormalizedName != null && x.NormalizedName.Contains(normalized));
        }

        var count = await query.CountAsync();
        var rows = await query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(await RoleDtos(rows), count, page, pageSize);
    }

    public async Task<RoleDto> GetRole(string id) => (await RoleDtos([await FindRole(id)]))[0];
    public async Task<RoleDto> CreateRole(SaveRoleRequest request, Actor actor)
    {
        var permissions = ValidateRoleRequest(request);
        await using var tx = await BeginChange(actor);
        var role = new IdentityRole(request.Name.Trim());
        Check(await roleManager.CreateAsync(role));
        db.RoleDefinitions.Add(new() { RoleId = role.Id, Description = Description(request.Description) });
        db.RolePermissions.AddRange(permissions.Select(x => new RolePermission { RoleId = role.Id, Permission = x }));
        AuditRole(actor, "role.create", role, new { after = RoleFacts(role.Name!, Description(request.Description), permissions) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetRole(role.Id);
    }

    public async Task<RoleDto> UpdateRole(string id, SaveRoleRequest request, Actor actor)
    {
        var permissions = ValidateRoleRequest(request);
        await using var tx = await BeginChange(actor);
        var role = await FindRole(id);
        var definition = await Definition(role);
        ProtectAdminRole(role, definition);
        var memberIds = await db.UserRoles.Where(x => x.RoleId == id).Select(x => x.UserId).ToArrayAsync();
        if (permissions.Contains(ApplicationPermissions.Administrator) && await db.Users.AnyAsync(x => memberIds.Contains(x.Id) && x.AccessChannelId != null))
            throw new RuleException("CHANNEL_PLATFORM_GRANT_FORBIDDEN", 409);
        var previous = await db.RolePermissions.Where(x => x.RoleId == id).OrderBy(x => x.Permission).ToArrayAsync();
        var oldPermissions = previous.Select(x => x.Permission).ToArray();
        var description = Description(request.Description);
        var before = RoleFacts(role.Name!, definition.Description, oldPermissions);
        if (role.Name != request.Name.Trim() || definition.Description != description || !oldPermissions.SequenceEqual(permissions))
        {
            role.Name = request.Name.Trim();
            definition.Description = description;
            // Identity updates normalized names and duplicate validation; the outer
            // transaction retains the global authorization lock across every save.
            Check(await roleManager.UpdateAsync(role));
            ReplaceGrants(id, previous, permissions);
            foreach (var member in await db.Users.Where(x => memberIds.Contains(x.Id)).ToArrayAsync())
                Check(await users.UpdateSecurityStampAsync(member));
            AuditRole(actor, "role.update", role, new { before, after = RoleFacts(role.Name!, description, permissions) });
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return await GetRole(id);
    }

    public async Task DeleteRole(string id, Actor actor)
    {
        await using var tx = await BeginChange(actor);
        var role = await FindRole(id);
        var definition = await Definition(role);
        ProtectAdminRole(role, definition);
        if (await db.UserRoles.AnyAsync(x => x.RoleId == id))
            throw new RuleException("ROLE_IN_USE", 409);
        var grants = await db.RolePermissions.Where(x => x.RoleId == id).Select(x => x.Permission).Order().ToArrayAsync();
        AuditRole(actor, "role.delete", role, new { before = RoleFacts(role.Name!, definition.Description, grants), after = (object? )null });
        Check(await roleManager.DeleteAsync(role));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    public async Task<AccountDto> ChangeRoles(string id, ChangeAccountRolesRequest request, Actor actor)
    {
        await using var tx = await BeginChange(actor);
        var selected = await ResolveRoles(null, request.RoleIds);
        var user = await Find(id);
        var before = await AccountFacts(user);
        if (await ReplaceMemberships(user, selected, actor))
        {
            Audit(actor, "account.roles", user, new { before, after = await AccountFacts(user) });
            await db.SaveChangesAsync();
        }

        await tx.CommitAsync();
        return Dto(user, selected);
    }

    private async Task<bool> ReplaceMemberships(AdminUser user, IdentityRole[] selected, Actor actor)
    {
        var selectedIds = selected.Select(x => x.Id).ToArray();
        var platform = await db.RolePermissions.AnyAsync(x => selectedIds.Contains(x.RoleId) && x.Permission == ApplicationPermissions.Administrator);
        if (user.AccessChannelId != null && platform)
            throw new RuleException("CHANNEL_PLATFORM_GRANT_FORBIDDEN", 409);
        if (user.Id == actor.Id && !platform)
            throw new RuleException("ACCOUNT_SELF_CHANGE_FORBIDDEN", 409);
        var oldRoles = await users.GetRolesAsync(user);
        if (!selected.Any(x => x.NormalizedName == "ADMIN"))
            await ProtectLastAdmin(user, oldRoles);
        var memberships = await db.UserRoles.Where(x => x.UserId == user.Id).ToArrayAsync();
        if (memberships.Select(x => x.RoleId).Order().SequenceEqual(selectedIds.Order()))
            return false;
        db.UserRoles.RemoveRange(memberships.Where(x => !selectedIds.Contains(x.RoleId)));
        db.UserRoles.AddRange(selectedIds.Except(memberships.Select(x => x.RoleId)).Select(roleId => new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId }));
        Check(await users.UpdateSecurityStampAsync(user));
        return true;
    }

    private async Task<IdentityRole[]> ResolveRoles(string? roleName, string[]? roleIds)
    {
        if (roleIds is not null)
        {
            if (roleName is not null || roleIds.Length is < 1 or > 20 || roleIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 450) || roleIds.Distinct(StringComparer.Ordinal).Count() != roleIds.Length)
                throw new RuleException("VALIDATION_ERROR", 400);
            var result = await db.Roles.Where(x => roleIds.Contains(x.Id)).ToArrayAsync();
            if (result.Length != roleIds.Length)
                throw new RuleException("VALIDATION_ERROR", 400);
            return result;
        }

        if (string.IsNullOrWhiteSpace(roleName) || roleName.Length > 64)
            throw new RuleException("VALIDATION_ERROR", 400);
        return[await roleManager.FindByNameAsync(roleName) ?? throw new RuleException("VALIDATION_ERROR", 400)];
    }

    private async Task<RoleDto[]> RoleDtos(IdentityRole[] roles)
    {
        var ids = roles.Select(x => x.Id).ToArray();
        var definitions = await db.RoleDefinitions.AsNoTracking().Where(x => ids.Contains(x.RoleId)).ToDictionaryAsync(x => x.RoleId);
        var grants = (await db.RolePermissions.AsNoTracking().Where(x => ids.Contains(x.RoleId)).ToArrayAsync()).ToLookup(x => x.RoleId, x => x.Permission);
        var members = await db.UserRoles.AsNoTracking().Where(x => ids.Contains(x.RoleId)).GroupBy(x => x.RoleId).Select(x => new { RoleId = x.Key, Count = x.Count() }).ToDictionaryAsync(x => x.RoleId, x => x.Count);
        return roles.Select(x => new RoleDto(x.Id, x.Name!, definitions.GetValueOrDefault(x.Id)?.Description, definitions.GetValueOrDefault(x.Id)?.IsBuiltIn ?? false, grants[x.Id].Order(StringComparer.Ordinal).ToArray(), members.GetValueOrDefault(x.Id))).ToArray();
    }

    private async Task<object> AccountFacts(AdminUser user)
    {
        var assigned = await AssignedRoles(user.Id);
        var current = await RbacAccess.Resolve(db, user);
        return new
        {
            userName = user.UserName,
            roles = RoleNames(assigned),
            permissions = current.Permissions,
            allApplications = current.AllApplications,
            applicationIds = current.ApplicationIds,
            channelId = current.ChannelId
        };
    }

    private static object RoleNames(IEnumerable<IdentityRole> roles) => roles.OrderBy(x => x.Name, StringComparer.Ordinal).Select(x => new { id = x.Id, name = x.Name }).ToArray();
    private static object RoleFacts(string name, string? description, string[] permissions) => new
    {
        name,
        description,
        permissions
    };
    private static string? Description(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string[] ValidateRoleRequest(SaveRoleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 64 || request.Description?.Length > 500 || request.Permissions is null || request.Permissions.Length > ApplicationPermissions.All.Length || request.Permissions.Any(x => !ApplicationPermissions.All.Contains(x)))
            throw new RuleException("VALIDATION_ERROR", 400);
        return request.Permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private async Task<IdentityRole> FindRole(string id) => await roleManager.FindByIdAsync(id) ?? throw new RuleException("ROLE_NOT_FOUND", 404);
    private async Task<RoleDefinition> Definition(IdentityRole role)
    {
        var definition = await db.RoleDefinitions.SingleOrDefaultAsync(x => x.RoleId == role.Id);
        if (definition is not null)
            return definition;
        definition = new()
        {
            RoleId = role.Id
        };
        db.RoleDefinitions.Add(definition);
        return definition;
    }

    private static void ProtectAdminRole(IdentityRole role, RoleDefinition definition)
    {
        if (definition.IsBuiltIn && role.NormalizedName == "ADMIN")
            throw new RuleException("BUILT_IN_ROLE_PROTECTED", 409);
    }

    private void ReplaceGrants(string roleId, RolePermission[] previous, string[] permissions)
    {
        db.RolePermissions.RemoveRange(previous.Where(x => !permissions.Contains(x.Permission)));
        db.RolePermissions.AddRange(permissions.Except(previous.Select(x => x.Permission), StringComparer.Ordinal).Select(x => new RolePermission { RoleId = roleId, Permission = x }));
    }

    private void AuditRole(Actor actor, string action, IdentityRole role, object changes) => db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = Guid.Parse(role.Id), TargetType = "role", TargetName = role.Name, Changes = JsonSerializer.Serialize(changes), CreatedAt = clock.GetUtcNow(), TraceId = actor.TraceId });
}
