using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class RecipientAccessService(AppDbContext db)
{
    public async Task<string[]> Eligible(Guid applicationId, string[] ids, CancellationToken cancellationToken = default, Guid? channelId = null, bool forEvent = false)
    {
        if (ids.Length == 0)
            return[];
        var staff = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id) && x.DisabledAt == null).ToArrayAsync(cancellationToken);
        var grants = await (
            from membership in db.UserRoles.AsNoTracking()join grant in db.RolePermissions.AsNoTracking()on membership.RoleId equals grant.RoleId
            where ids.Contains(membership.UserId)select new
            {
                membership.UserId,
                grant.Permission
            }

        ).Distinct().ToArrayAsync(cancellationToken);
        var channels = await db.Channels.AsNoTracking().Where(x => x.ApplicationIds.Contains(applicationId)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        return staff.Where(user =>
        {
            var scope = RbacAccess.Scope(user, grants.Where(x => x.UserId == user.Id).Select(x => x.Permission));
            return scope.Permissions.Length > 0 && (scope.AllApplications || scope.ApplicationIds.Contains(applicationId)) && (scope.ChannelId == null || channels.Contains(scope.ChannelId.Value) && (!forEvent || scope.ChannelId == channelId));
        }).Select(x => x.Id).ToArray();
    }
}
