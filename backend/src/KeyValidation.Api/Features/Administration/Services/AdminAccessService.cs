using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
public sealed class AdminAccessService(AppDbContext db, UserManager<AdminUser> users, ApplicationAccessScope access, AccountSecurityService security)
{
    public async Task Initialize(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin") || context.User.Identity?.IsAuthenticated != true)
            return;
        var user = await users.GetUserAsync(context.User) ?? throw new RuleException("UNAUTHENTICATED", 401);
        var current = await RbacAccess.Resolve(db, user);
        access.Initialize(current, current.Permissions.Contains(ApplicationPermissions.Administrator), (permission, cancellationToken) => RecheckWrite(context, permission, cancellationToken));
    }

    private async Task RecheckWrite(HttpContext context, string permission, CancellationToken cancellationToken)
    {
        // Retain this shared transaction lock through commit. Account changes use
        // the exclusive form of the same lock, so a grant cannot change after this check.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(1262836049, 1)", cancellationToken);
        var id = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (user is null || user.DisabledAt != null || user.SecurityStamp != context.User.FindFirstValue(users.Options.ClaimsIdentity.SecurityStampClaimType))
            throw new RuleException("UNAUTHENTICATED", 401);
        if (user.MustChangePassword)
            throw new RuleException("PASSWORD_CHANGE_REQUIRED", 403);
        var current = await RbacAccess.Resolve(db, user, cancellationToken);
        if (!current.Permissions.Contains(permission))
            throw new RuleException("FORBIDDEN", 403);
        await security.RecheckAccountMutation(context);
    }
}
