using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class ApiCredentialService(AppDbContext db, ApplicationAccessScope access, CommerceWorkflow workflow)
{
    public static readonly string[] AllowedPermissions = [ApplicationPermissions.ReadLicenses, ApplicationPermissions.ManageCustomers, ApplicationPermissions.ManageOrders];
    public async Task<PagedResult<ApiCredentialDto>> List(int page, int pageSize, Guid? applicationId, Guid? channelId)
    {
        access.Require(ApplicationPermissions.Administrator); CommerceWorkflow.Page(page, pageSize);
        var query = db.ApiCredentials.AsNoTracking().Where(x => (applicationId == null || x.ApplicationId == applicationId) && (channelId == null || x.ChannelId == channelId));
        var count = await query.CountAsync(); var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(rows.Select(Dto).ToArray(), count, page, pageSize);
    }
    public async Task<ApiCredentialSecretDto> Create(CreateApiCredential request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator); var name = CommerceWorkflow.Text(request.Name); var permissions = Permissions(request.Permissions);
        await using var tx = await db.Database.BeginTransactionAsync();
        await workflow.LockApplication(request.ApplicationId, false); await workflow.LockChannel(request.ChannelId, request.ApplicationId, false); await AuthorizationLock();
        if (!await db.Applications.AnyAsync(x => x.Id == request.ApplicationId)) throw new RuleException("NOT_FOUND", 404);
        if (request.ChannelId != null && !await db.Channels.AnyAsync(x => x.Id == request.ChannelId && x.ApplicationIds.Contains(request.ApplicationId))) throw new RuleException("NOT_FOUND", 404);
        var secret = CommerceWorkflow.NewSecret("kga_"); var now = workflow.Now;
        var credential = new ApiCredential { ApplicationId = request.ApplicationId, ChannelId = request.ChannelId, Name = name, Permissions = permissions, SecretDigest = CommerceWorkflow.Digest(secret), CreatedAt = now, UpdatedAt = now };
        db.ApiCredentials.Add(credential); Audit(actor, "api-credential.create", credential);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new(Dto(credential), secret);
    }
    public async Task<ApiCredentialDto> Update(Guid id, UpdateApiCredential request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator); var name = CommerceWorkflow.Text(request.Name); var permissions = Permissions(request.Permissions);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(id); await AuthorizationLock();
        var credential = await Find(id); credential.Name = name; credential.Permissions = permissions; credential.IsEnabled = request.IsEnabled; credential.UpdatedAt = workflow.Now;
        Audit(actor, "api-credential.update", credential); await db.SaveChangesAsync(); await tx.CommitAsync(); return Dto(credential);
    }
    public async Task<ApiCredentialSecretDto> Rotate(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        await using var tx = await db.Database.BeginTransactionAsync(); await Lock(id); await AuthorizationLock();
        var credential = await Find(id); var secret = CommerceWorkflow.NewSecret("kga_"); credential.SecretDigest = CommerceWorkflow.Digest(secret); credential.UpdatedAt = workflow.Now;
        Audit(actor, "api-credential.rotate", credential); await db.SaveChangesAsync(); await tx.CommitAsync(); return new(Dto(credential), secret);
    }
    private static string[] Permissions(string[]? values)
    {
        CommerceWorkflow.Require(values is { Length: <= 3 } && values.All(x => AllowedPermissions.Contains(x, StringComparer.Ordinal)));
        return values!.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }
    private Task Lock(Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"ApiCredentials\" WHERE \"Id\" = {id} FOR UPDATE");
    private async Task AuthorizationLock()
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)"); await access.RecheckWrite(ApplicationPermissions.Administrator);
    }
    private async Task<ApiCredential> Find(Guid id) => await db.ApiCredentials.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    private void Audit(Actor actor, string action, ApiCredential x) => workflow.Audit(actor, action, x.Id, x.ApplicationId, x.ChannelId, new { x.Name, x.Permissions, x.IsEnabled, x.ChannelId }, x.Name);
    private static ApiCredentialDto Dto(ApiCredential x) => new(x.Id, x.ApplicationId, x.ChannelId, x.Name, x.Permissions, x.IsEnabled, x.CreatedAt, x.UpdatedAt);
}
