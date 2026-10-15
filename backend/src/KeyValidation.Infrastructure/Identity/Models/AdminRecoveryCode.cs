using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class AdminRecoveryCode
{
    public string UserId { get; set; } = "";
    public string Digest { get; set; } = "";
}
