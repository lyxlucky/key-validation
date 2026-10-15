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
