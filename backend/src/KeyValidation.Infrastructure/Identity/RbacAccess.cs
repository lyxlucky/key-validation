using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
// One stored grant source feeds request scope, commit rechecks and notification
// eligibility. Role names and the deprecated per-user permission array play no part.
public static class RbacAccess
{
    public static IQueryable<string> Permissions(AppDbContext db, string userId) => (
        from membership in db.UserRoles.AsNoTracking()join grant in db.RolePermissions.AsNoTracking()on membership.RoleId equals grant.RoleId
        where membership.UserId == userId && ApplicationPermissions.All.Contains(grant.Permission)select grant.Permission).Distinct();
    public static async Task<AccountAccessDto> Resolve(AppDbContext db, AdminUser user, CancellationToken cancellationToken = default) => Scope(user, await Permissions(db, user.Id).ToArrayAsync(cancellationToken));
    public static AccountAccessDto Scope(AdminUser user, IEnumerable<string> permissions) => new(permissions.Where(x => ApplicationPermissions.All.Contains(x) && (user.AccessChannelId == null || x != ApplicationPermissions.Administrator)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), user.AccessAllApplications, user.AccessAllApplications ? [] : user.AccessApplicationIds.ToArray(), user.AccessChannelId);
}
