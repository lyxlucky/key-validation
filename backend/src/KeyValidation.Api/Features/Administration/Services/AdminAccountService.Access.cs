using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
public sealed partial class AdminAccountService
{
    public async Task<AccountAccessDto> GetAccess(string id)
    {
        var user = await Find(id);
        return await RbacAccess.Resolve(db, user);
    }

    public async Task<AccountAccessDto> SetAccess(string id, UpdateAccountAccess request, Actor actor)
    {
        if (request.ApplicationIds is null || request.Permissions?.Length > ApplicationPermissions.All.Length || request.ApplicationIds.Length > 1000 || request.ApplicationIds.Contains(Guid.Empty) || request.ChannelId != null)
            throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await BeginChange(actor);
        var user = await Find(id);
        var roles = await users.GetRolesAsync(user);
        var current = await RbacAccess.Resolve(db, user);
        var before = await AccountFacts(user);
        var permissions = request.Permissions?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (permissions?.Any(x => !ApplicationPermissions.All.Contains(x, StringComparer.Ordinal)) == true)
            throw new RuleException("VALIDATION_ERROR", 400);
        if ((id == actor.Id || roles.Contains("Admin")) && (!request.AllApplications || permissions is not null && !permissions.SequenceEqual(current.Permissions)))
            throw new RuleException("ACCOUNT_ACCESS_RESTRICTION_FORBIDDEN", 409);
        var applications = request.AllApplications ? [] : request.ApplicationIds.Distinct().Order().ToArray();
        if (await db.Applications.CountAsync(x => applications.Contains(x.Id)) != applications.Length)
            throw new RuleException("VALIDATION_ERROR", 400);
        if (permissions is not null && !roles.Contains("Admin"))
            await SetManagedGrants(user, permissions, actor);
        user.AccessConfigured = !roles.Contains("Admin");
        user.AccessAllApplications = request.AllApplications;
        user.AccessApplicationIds = applications;
        user.AccessPermissions = [];
        Check(await users.UpdateSecurityStampAsync(user));
        Audit(actor, "account.access", user, new { before, after = await AccountFacts(user) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await RbacAccess.Resolve(db, user);
    }
}
