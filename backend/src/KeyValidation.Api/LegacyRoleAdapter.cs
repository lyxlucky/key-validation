using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;

public sealed partial class AdminAccountService
{
    // Only the compatibility endpoints call this adapter. Authorization always
    // reads the resulting ordinary role-permission and user-role associations.
    private async Task SetManagedGrants(AdminUser user, string[] permissions, Actor actor)
    {
        // Replacing every membership with a managed role also removes built-in Admin.
        await ProtectLastAdmin(user, await users.GetRolesAsync(user));
        permissions = permissions.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var definition = await db.RoleDefinitions.SingleOrDefaultAsync(x => x.LegacyUserId == user.Id);
        var created = definition is null;
        IdentityRole role;
        if (definition is null)
        {
            var name = user.UserName!; if (name.Length > 20) name = name[..20];
            role = new($"历史权限 · {name} · {Guid.Parse(user.Id):N}"); Check(await roleManager.CreateAsync(role));
            definition = new() { RoleId = role.Id, LegacyUserId = user.Id, Description = "旧版账号权限兼容角色；新界面可改为分配业务角色。" };
            db.RoleDefinitions.Add(definition);
        }
        else role = await FindRole(definition.RoleId);
        var old = await db.RolePermissions.Where(x => x.RoleId == role.Id).OrderBy(x => x.Permission).ToArrayAsync();
        var members = await db.UserRoles.Where(x => x.RoleId == role.Id).Select(x => x.UserId).ToArrayAsync();
        var affectedIds = members.Append(user.Id).Distinct().ToArray();
        if (permissions.Contains(ApplicationPermissions.Administrator) && await db.Users.AnyAsync(x => affectedIds.Contains(x.Id) && x.AccessChannelId != null)) throw new RuleException("CHANNEL_PLATFORM_GRANT_FORBIDDEN", 409);
        var changed = !old.Select(x => x.Permission).SequenceEqual(permissions);
        ReplaceGrants(role.Id, old, permissions);
        var memberships = await db.UserRoles.Where(x => x.UserId == user.Id).ToArrayAsync();
        db.UserRoles.RemoveRange(memberships.Where(x => x.RoleId != role.Id));
        if (!memberships.Any(x => x.RoleId == role.Id)) db.UserRoles.Add(new() { UserId = user.Id, RoleId = role.Id });
        foreach (var member in await db.Users.Where(x => x.Id == user.Id || changed && members.Contains(x.Id)).ToArrayAsync()) Check(await users.UpdateSecurityStampAsync(member));
        if (created || changed) AuditRole(actor, created ? "role.create" : "role.update", role,
            new { before = created ? null : RoleFacts(role.Name!, definition.Description, old.Select(x => x.Permission).ToArray()), after = RoleFacts(role.Name!, definition.Description, permissions) });
        await db.SaveChangesAsync();
    }
}
