using KeyValidation.Application;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    public DbSet<RoleDefinition> RoleDefinitions => Set<RoleDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    private static void ConfigureRbac(ModelBuilder b)
    {
        b.Entity<RoleDefinition>(e =>
        {
            e.ToTable("RoleDefinitions");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne<IdentityRole>().WithOne().HasForeignKey<RoleDefinition>(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AdminUser>().WithOne().HasForeignKey<RoleDefinition>(x => x.LegacyUserId).OnDelete(DeleteBehavior.SetNull);
        });
        b.Entity<RolePermission>(e =>
        {
            e.ToTable("RolePermissions");
            e.HasKey(x => new { x.RoleId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(80);
            e.HasOne<IdentityRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Permission, x.RoleId });
        });
    }
}
