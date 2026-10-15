using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed partial class AdminUser
{
    public bool MustChangePassword { get; set; }
    public string? ProtectedTotpSecret { get; set; }
    public string? ProtectedPendingTotpSecret { get; set; }
    public DateTimeOffset? PendingTotpExpiresAt { get; set; }
    public string? PendingTotpSecurityStamp { get; set; }
    public long? LastTotpStep { get; set; }
    public Guid? LoginChallengeId { get; set; }
}

public sealed class AdminRecoveryCode
{
    public string UserId { get; set; } = "";
    public string Digest { get; set; } = "";
}

public sealed partial class AppDbContext
{
    public DbSet<AdminRecoveryCode> AdminRecoveryCodes => Set<AdminRecoveryCode>();

    partial void ConfigureAccountSecurity(ModelBuilder b)
    {
        b.Entity<AdminUser>(e =>
        {
            // Existing and CLI-bootstrapped accounts retain their current access.
            e.Property(x => x.MustChangePassword).HasDefaultValue(false);
            e.Property(x => x.ProtectedTotpSecret).HasMaxLength(2048);
            e.Property(x => x.ProtectedPendingTotpSecret).HasMaxLength(2048);
            e.Property(x => x.PendingTotpSecurityStamp).HasMaxLength(256);
        });
        b.Entity<AdminRecoveryCode>(e =>
        {
            e.ToTable("AdminRecoveryCodes");
            e.HasKey(x => new { x.UserId, x.Digest });
            e.Property(x => x.Digest).HasMaxLength(64);
            e.HasOne<AdminUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
