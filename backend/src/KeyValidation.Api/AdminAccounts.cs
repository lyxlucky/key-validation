using System.Security.Claims;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KeyValidation.Api;

public record AccountDto(string Id, string UserName, string[] Roles, DateTimeOffset? DisabledAt, DateTimeOffset CreatedAt, bool MustChangePassword, bool TwoFactorEnabled, string[] RoleIds);
public record CreateAccountRequest(string UserName, string Password, string? Role = null, string[]? RoleIds = null);
public record ChangeAccountRoleRequest(string Role);
public record ChangeAccountRolesRequest(string[] RoleIds);
public record ResetAccountPasswordRequest(string Password);

public static class AdminAccounts
{
    public static void MapAccountApi(this WebApplication app)
    {
        var accounts = app.MapGroup("/api/admin/accounts").WithTags("Accounts").RequireAuthorization("Admin");
        accounts.MapGet("", (AdminAccountService service, int page = 1, int pageSize = 20, string? search = null) => service.List(page, pageSize, search));
        accounts.MapPost("", async (CreateAccountRequest request, HttpContext context, AdminAccountService service) =>
        {
            var account = await service.Create(request, ActorOf(context));
            return TypedResults.Created($"/api/admin/accounts/{account.Id}", account);
        });
        accounts.MapPatch("/{id}/role", (string id, ChangeAccountRoleRequest request, HttpContext context, AdminAccountService service) => service.ChangeRole(id, request, ActorOf(context)));
        accounts.MapPatch("/{id}/roles", (string id, ChangeAccountRolesRequest request, HttpContext context, AdminAccountService service) => service.ChangeRoles(id, request, ActorOf(context)));
        accounts.MapPost("/{id}/disable", async (string id, HttpContext context, AdminAccountService service) => { await service.SetDisabled(id, true, ActorOf(context)); return TypedResults.NoContent(); });
        accounts.MapPost("/{id}/enable", async (string id, HttpContext context, AdminAccountService service) => { await service.SetDisabled(id, false, ActorOf(context)); return TypedResults.NoContent(); });
        accounts.MapPost("/{id}/reset-password", async (string id, ResetAccountPasswordRequest request, HttpContext context, AdminAccountService service) => { await service.ResetPassword(id, request, ActorOf(context)); return TypedResults.NoContent(); });
    }

    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
}

public sealed partial class AdminAccountService(AppDbContext db, UserManager<AdminUser> users, TimeProvider clock, IHttpContextAccessor contexts, AccountSecurityService security, RoleManager<IdentityRole> roleManager)
{
    public async Task<PagedResult<AccountDto>> List(int page, int pageSize, string? search)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100 || search?.Length > 128) throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalized = users.NormalizeName(search.Trim())!;
            query = query.Where(user => user.NormalizedUserName != null && user.NormalizedUserName.Contains(normalized));
        }
        var total = await query.CountAsync();
        var rows = await query.OrderByDescending(user => user.CreatedAt).ThenBy(user => user.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        var ids = rows.Select(user => user.Id).ToArray();
        var memberships = await (from membership in db.UserRoles.AsNoTracking()
                                 join role in db.Roles.AsNoTracking() on membership.RoleId equals role.Id
                                 where ids.Contains(membership.UserId)
                                 select new { membership.UserId, Role = role }).ToListAsync();
        var roles = memberships.ToLookup(membership => membership.UserId, membership => membership.Role);
        return new(rows.Select(user => Dto(user, roles[user.Id])).ToArray(), total, page, pageSize);
    }

    public async Task<AccountDto> Create(CreateAccountRequest request, Actor actor)
    {
        ValidatePassword(request.Password);
        var name = request.UserName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 128) throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await BeginChange(actor);
        var selected = await ResolveRoles(request.Role, request.RoleIds);
        var user = new AdminUser { UserName = name, CreatedAt = clock.GetUtcNow(), MustChangePassword = true };
        Check(await users.CreateAsync(user, request.Password));
        db.UserRoles.AddRange(selected.Select(role => new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id }));
        Audit(actor, "account.create", user, new { after = new { userName = user.UserName, roles = RoleNames(selected) } });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return Dto(user, selected);
    }

    public async Task<AccountDto> ChangeRole(string id, ChangeAccountRoleRequest request, Actor actor)
    {
        await using var tx = await BeginChange(actor);
        var user = await Find(id);
        var requested = await ResolveRoles(request.Role, null);
        var before = await AccountFacts(user);
        var managed = await db.RoleDefinitions.SingleOrDefaultAsync(x => x.LegacyUserId == id && db.UserRoles.Any(m => m.UserId == id && m.RoleId == x.RoleId));
        if (managed is not null)
        {
            // The old endpoint used a ceiling. Preserve an existing legacy
            // restriction by adapting into stored grants, never a runtime ceiling.
            var requestedIds = requested.Select(x => x.Id).ToArray();
            var upper = await db.RolePermissions.Where(x => requestedIds.Contains(x.RoleId)).Select(x => x.Permission).ToArrayAsync();
            var current = await RbacAccess.Resolve(db, user);
            var restricted = current.Permissions.Intersect(upper, StringComparer.Ordinal).ToArray();
            if (id == actor.Id && !restricted.Contains(ApplicationPermissions.Administrator)) throw new RuleException("ACCOUNT_SELF_CHANGE_FORBIDDEN", 409);
            await SetManagedGrants(user, restricted, actor);
        }
        else await ReplaceMemberships(user, requested, actor);
        Audit(actor, "account.role", user, new { before, after = await AccountFacts(user), requestedRole = request.Role });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await AccountDtoOf(user);
    }

    public async Task SetDisabled(string id, bool disabled, Actor actor)
    {
        await using var tx = await BeginChange(actor);
        var user = await Find(id);
        if (disabled && id == actor.Id) throw new RuleException("ACCOUNT_SELF_CHANGE_FORBIDDEN", 409);
        if (disabled) await ProtectLastAdmin(user, await users.GetRolesAsync(user));
        if ((user.DisabledAt != null) != disabled)
        {
            user.DisabledAt = disabled ? clock.GetUtcNow() : null;
            // Rotate on both transitions: restoring access must never restore a previous session.
            Check(await users.UpdateSecurityStampAsync(user));
            Audit(actor, disabled ? "account.disable" : "account.enable", user);
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
    }

    public async Task ResetPassword(string id, ResetAccountPasswordRequest request, Actor actor)
    {
        ValidatePassword(request.Password);
        await using var tx = await BeginChange(actor);
        var user = await Find(id);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        user.MustChangePassword = true;
        user.LoginChallengeId = null;
        AccountSecurityService.ClearPending(user);
        // Identity validates the password and changes the security stamp with the password hash.
        Check(await users.ResetPasswordAsync(user, token, request.Password));
        Check(await users.SetLockoutEndDateAsync(user, null));
        Check(await users.ResetAccessFailedCountAsync(user));
        Audit(actor, "account.reset-password", user);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
    }

    private async Task<IDbContextTransaction> BeginChange(Actor actor)
    {
        var tx = await db.Database.BeginTransactionAsync();
        try
        {
            // The two-key advisory-lock namespace is distinct from issuance's bigint keys.
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
            // Cookie validation may have tracked the actor before waiting for this lock.
            // Reload all account state after serialization, including the actor's authority.
            db.ChangeTracker.Clear();
            var author = await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == actor.Id);
            if (author is null || author.DisabledAt != null || author.AccessChannelId != null ||
                !await RbacAccess.Permissions(db, actor.Id).AnyAsync(permission => permission == ApplicationPermissions.Administrator)) throw new RuleException("FORBIDDEN", 403);
            if (contexts.HttpContext is { } context) await security.RecheckAccountMutation(context);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    private IQueryable<AdminUser> EnabledAdmins() =>
        from user in db.Users.AsNoTracking()
        join membership in db.UserRoles on user.Id equals membership.UserId
        join role in db.Roles on membership.RoleId equals role.Id
        join definition in db.RoleDefinitions on role.Id equals definition.RoleId
        where user.DisabledAt == null && role.NormalizedName == "ADMIN" && definition.IsBuiltIn
        select user;

    private async Task ProtectLastAdmin(AdminUser user, IList<string> roles)
    {
        if (user.DisabledAt == null && roles.Contains("Admin") && await EnabledAdmins().CountAsync() <= 1)
            throw new RuleException("LAST_ADMIN_REQUIRED", 409);
    }

    private async Task<AdminUser> Find(string id) => await users.FindByIdAsync(id) ?? throw new RuleException("ACCOUNT_NOT_FOUND", 404);
    private static AccountDto Dto(AdminUser user, IEnumerable<IdentityRole> assigned)
    {
        var roles = assigned.OrderBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
        return new(user.Id, user.UserName!, roles.Select(x => x.Name!).ToArray(), user.DisabledAt, user.CreatedAt, user.MustChangePassword, user.TwoFactorEnabled, roles.Select(x => x.Id).ToArray());
    }
    private async Task<AccountDto> AccountDtoOf(AdminUser user) => Dto(user, await AssignedRoles(user.Id));
    private Task<IdentityRole[]> AssignedRoles(string id) => (from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id where membership.UserId == id select role).ToArrayAsync();
    private static void ValidatePassword(string password) { if (string.IsNullOrEmpty(password) || password.Length > 1024) throw new RuleException("VALIDATION_ERROR", 400); }
    private static void Check(IdentityResult result)
    {
        if (result.Succeeded) return;
        if (result.Errors.Any(error => error.Code is "DuplicateUserName" or "DuplicateEmail" or "DuplicateRoleName" or "ConcurrencyFailure")) throw new RuleException("RESOURCE_CONFLICT", 409);
        throw new RuleException("VALIDATION_ERROR", 400);
    }

    private void Audit(Actor actor, string action, AdminUser user, object? changes = null) => db.AuditLogs.Add(new AdminAuditLog
    {
        ActorId = actor.Id, Action = action, TargetId = Guid.Parse(user.Id),
        TargetType = "account", TargetName = user.UserName, Changes = JsonSerializer.Serialize(changes ?? new { userName = user.UserName }),
        CreatedAt = clock.GetUtcNow(), TraceId = actor.TraceId
    });
}
