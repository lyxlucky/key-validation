using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;

public static class AccessEndpoints
{
    public static void MapAccessApi(this WebApplication app)
    {
        app.MapGet("/api/admin/auth/access", (ApplicationAccessScope access) => access.ToDto())
            .WithTags("Account access").RequireAuthorization("Read");
        var accounts = app.MapGroup("/api/admin/accounts").WithTags("Account access").RequireAuthorization("Admin");
        accounts.MapGet("/{id}/access", (string id, AdminAccountService service) => service.GetAccess(id));
        accounts.MapPut("/{id}/access", (string id, UpdateAccountAccess request, HttpContext context, AdminAccountService service) =>
            service.SetAccess(id, request, new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier)));
        app.MapPost("/api/admin/licenses/query", (LicenseQuery request, LicensingService service) => service.QueryLicenses(request))
            .WithTags("License management").RequireAuthorization("Read", ApplicationPermissions.ReadLicenses);
        app.MapGet("/api/admin/reports/operations", (LicensingService service, DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? applicationId = null, Guid? planId = null, Guid? channelId = null) =>
            service.OperationsReport(from, to, applicationId, planId, channelId)).WithTags("Operations reports").RequireAuthorization("Read", ApplicationPermissions.ReadReports);
        app.MapGet("/api/admin/reports/catalog", (LicensingService service) => service.ReportsCatalog())
            .WithTags("Operations reports").RequireAuthorization("Read", ApplicationPermissions.ReadReports);
    }
}

public sealed class AdminAccessService(AppDbContext db, UserManager<AdminUser> users, ApplicationAccessScope access, AccountSecurityService security)
{
    public async Task Initialize(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/api/admin") || context.User.Identity?.IsAuthenticated != true) return;
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
        if (user.MustChangePassword) throw new RuleException("PASSWORD_CHANGE_REQUIRED", 403);
        var current = await RbacAccess.Resolve(db, user, cancellationToken);
        if (!current.Permissions.Contains(permission))
            throw new RuleException("FORBIDDEN", 403);
        await security.RecheckAccountMutation(context);
    }
}

public sealed partial class AdminAccountService
{
    public async Task<AccountAccessDto> GetAccess(string id)
    {
        var user = await Find(id);
        return await RbacAccess.Resolve(db, user);
    }

    public async Task<AccountAccessDto> SetAccess(string id, UpdateAccountAccess request, Actor actor)
    {
        if (request.ApplicationIds is null || request.Permissions?.Length > ApplicationPermissions.All.Length ||
            request.ApplicationIds.Length > 1000 || request.ApplicationIds.Contains(Guid.Empty) || request.ChannelId != null)
            throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await BeginChange(actor);
        var user = await Find(id); var roles = await users.GetRolesAsync(user);
        var current = await RbacAccess.Resolve(db, user); var before = await AccountFacts(user);
        var permissions = request.Permissions?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (permissions?.Any(x => !ApplicationPermissions.All.Contains(x, StringComparer.Ordinal)) == true) throw new RuleException("VALIDATION_ERROR", 400);
        if ((id == actor.Id || roles.Contains("Admin")) && (!request.AllApplications || permissions is not null && !permissions.SequenceEqual(current.Permissions)))
            throw new RuleException("ACCOUNT_ACCESS_RESTRICTION_FORBIDDEN", 409);
        var applications = request.AllApplications ? [] : request.ApplicationIds.Distinct().Order().ToArray();
        if (await db.Applications.CountAsync(x => applications.Contains(x.Id)) != applications.Length) throw new RuleException("VALIDATION_ERROR", 400);
        if (permissions is not null && !roles.Contains("Admin")) await SetManagedGrants(user, permissions, actor);
        user.AccessConfigured = !roles.Contains("Admin"); user.AccessAllApplications = request.AllApplications;
        user.AccessApplicationIds = applications; user.AccessPermissions = [];
        Check(await users.UpdateSecurityStampAsync(user));
        Audit(actor, "account.access", user, new { before, after = await AccountFacts(user) });
        await db.SaveChangesAsync(); await tx.CommitAsync();
        return await RbacAccess.Resolve(db, user);
    }
}
