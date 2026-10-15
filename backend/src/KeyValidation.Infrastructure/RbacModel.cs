using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class RoleDefinition
{
    public string RoleId { get; set; } = "";
    public string? Description { get; set; }
    public bool IsBuiltIn { get; set; }
    // A legacy API configuration owns one explicit role, never an extra grant engine.
    public string? LegacyUserId { get; set; }
}

public sealed class RolePermission
{
    public string RoleId { get; set; } = "";
    public string Permission { get; set; } = "";
}

public sealed partial class AppDbContext
{
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    private static void ConfigureRbac(ModelBuilder b)
    {
        b.Entity<RoleDefinition>(e =>
        {
            e.ToTable("RoleDefinitions"); e.HasKey(x => x.RoleId); e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne<IdentityRole>().WithOne().HasForeignKey<RoleDefinition>(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AdminUser>().WithOne().HasForeignKey<RoleDefinition>(x => x.LegacyUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions"); e.HasKey(x => new { x.RoleId, x.Permission }); e.Property(x => x.Permission).HasMaxLength(80);
            e.HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Permission, x.RoleId });
        });
    }
}

// One stored grant source feeds request scope, commit rechecks and notification
// eligibility. Role names and the deprecated per-user permission array play no part.
public static class RbacAccess
{
    public static IQueryable<string> Permissions(AppDbContext db, string userId) =>
        (from membership in db.UserRoles.AsNoTracking()
         join grant in db.RolePermissions.AsNoTracking() on membership.RoleId equals grant.RoleId
         where membership.UserId == userId && ApplicationPermissions.All.Contains(grant.Permission)
         select grant.Permission).Distinct();

    public static async Task<AccountAccessDto> Resolve(AppDbContext db, AdminUser user, CancellationToken cancellationToken = default) =>
        Scope(user, await Permissions(db, user.Id).ToArrayAsync(cancellationToken));

    public static AccountAccessDto Scope(AdminUser user, IEnumerable<string> permissions) => new(
        permissions.Where(x => ApplicationPermissions.All.Contains(x) && (user.AccessChannelId == null || x != ApplicationPermissions.Administrator))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
        user.AccessAllApplications, user.AccessAllApplications ? [] : user.AccessApplicationIds.ToArray(), user.AccessChannelId);
}
