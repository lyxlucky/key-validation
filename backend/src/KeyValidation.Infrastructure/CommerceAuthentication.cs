using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class CommerceIdentity
{
    public string ActorId { get; internal set; } = "";
    public Guid? CustomerId { get; internal set; }
    public Guid? SessionId { get; internal set; }
    public Guid? CredentialId { get; internal set; }
}

public sealed class CredentialRateLimiter(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, (DateTimeOffset Start, int Count)> windows = [];
    public bool Admit(Guid credentialId)
    {
        lock (gate)
        {
            var now = clock.GetUtcNow();
            if (!windows.TryGetValue(credentialId, out var window) || now >= window.Start.AddMinutes(1)) window = (now, 0);
            if (window.Count >= 60) return false;
            windows[credentialId] = (window.Start, window.Count + 1);
            if (windows.Count > 10000) foreach (var id in windows.Where(x => now >= x.Value.Start.AddMinutes(1)).Select(x => x.Key).ToArray()) windows.Remove(id);
            return true;
        }
    }
}

public sealed class CommerceAuthentication(AppDbContext db, ApplicationAccessScope access, CommerceWorkflow workflow, CommerceIdentity identity, CredentialRateLimiter limiter)
{
    public async Task AuthenticateApi(string token)
    {
        var digest = TokenDigest(token, "kga_");
        var credential = await db.ApiCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.SecretDigest == digest && x.IsEnabled) ?? throw Unauthorized();
        await ValidBusinessAccess(credential.ApplicationId, credential.ChannelId);
        if (!limiter.Admit(credential.Id)) throw new RuleException("RATE_LIMITED", 429);
        identity.CredentialId = credential.Id; identity.ActorId = "api:" + credential.Id;
        access.Initialize(new(credential.Permissions.Intersect(ApiCredentialService.AllowedPermissions).ToArray(), false, [credential.ApplicationId], credential.ChannelId), false,
            (permission, cancellationToken) => RecheckApi(credential.Id, digest, permission, cancellationToken));
    }
    private async Task RecheckApi(Guid id, string digest, string permission, CancellationToken cancellationToken)
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(1262836049, 1)", cancellationToken);
        var current = await db.ApiCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.SecretDigest == digest && x.IsEnabled, cancellationToken) ?? throw Unauthorized();
        await ValidBusinessAccess(current.ApplicationId, current.ChannelId);
        if (!current.Permissions.Contains(permission) || !ApiCredentialService.AllowedPermissions.Contains(permission)) throw new RuleException("FORBIDDEN", 403);
    }
    public async Task AuthenticatePortal(string token)
    {
        var digest = TokenDigest(token, "kgp_");
        var (session, customer) = await Portal(digest);
        identity.CustomerId = customer.Id; identity.SessionId = session.Id; identity.ActorId = "portal:" + customer.Id;
        access.Initialize(new([ApplicationPermissions.ReadLicenses, ApplicationPermissions.ManageCustomers, ApplicationPermissions.ManageOrders], false, [customer.ApplicationId], customer.ChannelId), false,
            async (_, cancellationToken) =>
            {
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock_shared(1262836049, 1)", cancellationToken);
                await Portal(digest);
            });
        access.RestrictToCustomer(customer.Id);
    }
    private async Task<(PortalSession, Customer)> Portal(string digest)
    {
        var now = workflow.Now;
        var session = await db.PortalSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenDigest == digest && x.RevokedAt == null && x.ExpiresAt > now) ?? throw Unauthorized();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == session.CustomerId && x.DisabledAt == null && x.AnonymizedAt == null && x.AccessCodeDigest != null && x.AccessVersion == session.AccessVersion) ?? throw Unauthorized();
        await ValidBusinessAccess(customer.ApplicationId, customer.ChannelId); return (session, customer);
    }
    public async Task<PortalSessionDto> Login(CreatePortalSession request)
    {
        var digest = TokenDigest(request.AccessCode, "kgc_");
        var location = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.AccessCodeDigest == digest && x.DisabledAt == null && x.AnonymizedAt == null) ?? throw Unauthorized();
        await using var tx = await db.Database.BeginTransactionAsync();
        await workflow.LockApplication(location.ApplicationId, false); await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        var customer = await workflow.LockCustomer(location.Id, false);
        if (customer.AccessCodeDigest != digest || customer.DisabledAt != null || customer.AnonymizedAt != null) throw Unauthorized();
        await ValidBusinessAccess(customer.ApplicationId, customer.ChannelId);
        var token = CommerceWorkflow.NewSecret("kgp_"); var now = workflow.Now;
        var session = new PortalSession { CustomerId = customer.Id, TokenDigest = CommerceWorkflow.Digest(token), AccessVersion = customer.AccessVersion, CreatedAt = now, ExpiresAt = now.AddHours(8) };
        db.PortalSessions.Add(session); await db.SaveChangesAsync(); await tx.CommitAsync(); return new(token, session.ExpiresAt, CommerceWorkflow.CustomerDto(customer));
    }
    public async Task Logout()
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); await using var tx = await db.Database.BeginTransactionAsync();
        // Revocation waits for authorized writes to commit; never upgrade a shared recheck lock.
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
        await access.RecheckWrite(ApplicationPermissions.ManageOrders);
        var session = await db.PortalSessions.SingleOrDefaultAsync(x => x.Id == identity.SessionId) ?? throw Unauthorized();
        session.RevokedAt = workflow.Now; await db.SaveChangesAsync(); await tx.CommitAsync();
    }
    private async Task ValidBusinessAccess(Guid applicationId, Guid? channelId)
    {
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId && x.DisabledAt == null)) throw Unauthorized();
        if (channelId != null && !await db.Channels.AnyAsync(x => x.Id == channelId && x.IsEnabled && x.ApplicationIds.Contains(applicationId))) throw Unauthorized();
    }
    private static string TokenDigest(string? token, string prefix)
    {
        if (token is null || token.Length != 47 || !token.StartsWith(prefix, StringComparison.Ordinal)) throw Unauthorized();
        return CommerceWorkflow.Digest(token);
    }
    private static RuleException Unauthorized() => new("UNAUTHENTICATED", 401);
}
