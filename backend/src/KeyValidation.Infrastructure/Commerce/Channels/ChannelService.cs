using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
public sealed class ChannelService(AppDbContext db, ApplicationAccessScope access, CommerceWorkflow workflow, UserManager<AdminUser> users, PlanAvailability planAvailability)
{
    public async Task<PagedResult<ChannelDto>> List(int page, int pageSize, string? search)
    {
        access.Require(ApplicationPermissions.Administrator);
        CommerceWorkflow.Page(page, pageSize);
        search = CommerceWorkflow.Optional(search, 120);
        var query = Visible().AsNoTracking();
        if (search != null)
            query = query.Where(x => x.Name.Contains(search));
        var total = await query.CountAsync();
        var channels = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(await Dtos(channels), total, page, pageSize);
    }

    public async Task<ChannelDto> Get(Guid id)
    {
        access.Require(ApplicationPermissions.Administrator);
        return (await Dtos([await Find(id)]))[0];
    }

    public async Task<ChannelDto> Create(CreateChannel request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        var name = CommerceWorkflow.Text(request.Name);
        CommerceWorkflow.Require(request.InitialQuota is >= 0 and <= CommerceWorkflow.MaxExactInteger);
        var applications = await Applications(request.ApplicationIds);
        await using var tx = await db.Database.BeginTransactionAsync();
        await AuthorizationLock();
        var account = await Account(request.AccountId, null);
        var now = workflow.Now;
        var channel = new Channel
        {
            Name = name,
            AccountId = account.Id,
            ApplicationIds = applications,
            RemainingQuota = request.InitialQuota,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Channels.Add(channel);
        account.AccessChannelId = channel.Id;
        await Stamp(account);
        if (request.InitialQuota > 0)
            db.ChannelQuotaEntries.Add(new() { ChannelId = channel.Id, Kind = "initial", Delta = request.InitialQuota, BalanceAfter = request.InitialQuota, Reason = "Initial quota", ActorId = actor.Id, CreatedAt = now, RequestId = channel.Id });
        workflow.Audit(actor, "channel.create", channel.Id, null, channel.Id, new { channel.Name, channel.AccountId, channel.ApplicationIds, channel.RemainingQuota, channel.IsEnabled }, channel.Name);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(channel.Id);
    }

    public async Task<ChannelDto> Update(Guid id, UpdateChannel request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        var name = CommerceWorkflow.Text(request.Name);
        var applications = await Applications(request.ApplicationIds);
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(id);
        await AuthorizationLock();
        var channel = await Visible().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        var account = await Account(request.AccountId, id);
        var before = Facts(channel);
        if (channel.AccountId != account.Id)
        {
            var prior = await db.Users.SingleAsync(x => x.Id == channel.AccountId);
            prior.AccessChannelId = null;
            await Stamp(prior);
        }

        account.AccessChannelId = id;
        await Stamp(account);
        channel.Name = name;
        channel.AccountId = account.Id;
        channel.ApplicationIds = applications;
        channel.IsEnabled = request.IsEnabled;
        channel.UpdatedAt = workflow.Now;
        workflow.Audit(actor, "channel.update", id, null, id, new { before, after = Facts(channel) }, channel.Name);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(id);
    }

    public async Task<ChannelDto> AdjustQuota(Guid id, AdjustChannelQuota request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        var reason = CommerceWorkflow.Text(request.Reason, 500);
        CommerceWorkflow.Require(request.RequestId != Guid.Empty && request.Delta != 0 && request.Delta is >= -CommerceWorkflow.MaxExactInteger and <= CommerceWorkflow.MaxExactInteger);
        var fingerprint = CommerceWorkflow.Fingerprint(new { request.Delta, reason });
        await using var tx = await db.Database.BeginTransactionAsync();
        await Lock(id);
        await AuthorizationLock();
        var channel = await Visible().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        var previous = await db.ChannelQuotaEntries.AsNoTracking().SingleOrDefaultAsync(x => x.ChannelId == id && x.Kind == "adjustment" && x.RequestId == request.RequestId);
        if (previous != null)
        {
            if (previous.Fingerprint != fingerprint)
                throw new RuleException("IDEMPOTENCY_CONFLICT", 409);
            return await Get(id);
        }

        long balance;
        try
        {
            balance = checked(channel.RemainingQuota + request.Delta);
        }
        catch (OverflowException)
        {
            throw new RuleException("VALIDATION_ERROR", 400);
        }

        if (balance < 0)
            throw new RuleException("CHANNEL_QUOTA_EXHAUSTED", 409);
        CommerceWorkflow.Require(balance <= CommerceWorkflow.MaxExactInteger);
        channel.RemainingQuota = balance;
        channel.UpdatedAt = workflow.Now;
        db.ChannelQuotaEntries.Add(new() { ChannelId = id, Kind = "adjustment", Delta = request.Delta, BalanceAfter = balance, Reason = reason, ActorId = actor.Id, CreatedAt = workflow.Now, RequestId = request.RequestId, Fingerprint = fingerprint });
        workflow.Audit(actor, "channel.quota", id, null, id, new { request.Delta, balance, reason, request.RequestId }, channel.Name);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(id);
    }

    public async Task<ChannelPriceDto> SetPrice(Guid id, SetChannelPrice request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.Administrator);
        CommerceWorkflow.Require(request.UnitPriceCents is >= 0 and <= CommerceWorkflow.MaxUnitPriceCents);
        await using var tx = await db.Database.BeginTransactionAsync();
        await workflow.LockApplication(request.ApplicationId, false);
        await Lock(id);
        await planAvailability.Require(request.ApplicationId, request.PlanId, requireEnabled: false);
        await AuthorizationLock();
        var channel = await Find(id);
        if (!channel.ApplicationIds.Contains(request.ApplicationId))
            throw new RuleException("NOT_FOUND", 404);
        var price = await db.ChannelPrices.SingleOrDefaultAsync(x => x.ChannelId == id && x.ApplicationId == request.ApplicationId && x.PlanId == request.PlanId);
        var before = price?.UnitPriceCents;
        if (price is null)
        {
            price = new ChannelPrice
            {
                ChannelId = id,
                ApplicationId = request.ApplicationId,
                PlanId = request.PlanId
            };
            db.ChannelPrices.Add(price);
        }

        price.UnitPriceCents = request.UnitPriceCents;
        workflow.Audit(actor, "channel.price", id, request.ApplicationId, id, new { request.PlanId, before, after = price.UnitPriceCents }, channel.Name);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(price.ApplicationId, price.PlanId, price.UnitPriceCents);
    }

    public async Task<PagedResult<ChannelQuotaDto>> Ledger(Guid id, int page, int pageSize)
    {
        access.Require(ApplicationPermissions.Administrator);
        CommerceWorkflow.Page(page, pageSize);
        await Find(id);
        var query = db.ChannelQuotaEntries.AsNoTracking().Where(x => x.ChannelId == id);
        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(rows.Select(x => new ChannelQuotaDto(x.Id, x.ChannelId, x.Kind, x.Delta, x.BalanceAfter, x.Reason, x.ActorId, x.CreatedAt, x.OrderId, x.RequestId)).ToArray(), count, page, pageSize);
    }

    private IQueryable<Channel> Visible() => db.Channels.Where(x => access.AllApplications || x.ApplicationIds.All(id => access.ApplicationIds.Contains(id)));
    private async Task<Channel> Find(Guid id) => await Visible().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    private Task Lock(Guid id) => db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"t_channels\" WHERE \"Id\" = {id} FOR UPDATE");
    private async Task AuthorizationLock()
    {
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(1262836049, 1)");
        await access.RecheckWrite(ApplicationPermissions.Administrator);
    }

    private async Task<AdminUser> Account(string id, Guid? channelId)
    {
        var account = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.DisabledAt == null) ?? throw new RuleException("ACCOUNT_NOT_FOUND", 404);
        if ((await users.GetRolesAsync(account)).Contains("Admin") || await RbacAccess.Permissions(db, id).AnyAsync(x => x == ApplicationPermissions.Administrator))
            throw new RuleException("CHANNEL_PLATFORM_GRANT_FORBIDDEN", 409);
        if (account.AccessChannelId != null && account.AccessChannelId != channelId || await db.Channels.AnyAsync(x => x.AccountId == id && x.Id != channelId))
            throw new RuleException("ACCOUNT_CHANNEL_CONFLICT", 409);
        return account;
    }

    private async Task<Guid[]> Applications(Guid[]? values)
    {
        CommerceWorkflow.Require(values is { Length: >= 1 and <= 1000 } && !values.Contains(Guid.Empty));
        var ids = values!.Distinct().ToArray();
        if (await db.Applications.CountAsync(x => ids.Contains(x.Id)) != ids.Length)
            throw new RuleException("NOT_FOUND", 404);
        return ids;
    }

    private async Task Stamp(AdminUser account)
    {
        var result = await users.UpdateSecurityStampAsync(account);
        if (!result.Succeeded)
            throw new RuleException("ACCOUNT_UPDATE_FAILED", 409);
    }

    private async Task<ChannelDto[]> Dtos(Channel[] channels)
    {
        var ids = channels.Select(x => x.Id).ToArray();
        var prices = await db.ChannelPrices.AsNoTracking().Where(x => ids.Contains(x.ChannelId)).OrderBy(x => x.ApplicationId).ThenBy(x => x.PlanId).ToArrayAsync();
        return channels.Select(x => new ChannelDto(x.Id, x.Name, x.AccountId, x.ApplicationIds, x.RemainingQuota, x.IsEnabled, x.CreatedAt, x.UpdatedAt, prices.Where(p => p.ChannelId == x.Id).Select(p => new ChannelPriceDto(p.ApplicationId, p.PlanId, p.UnitPriceCents)).ToArray())).ToArray();
    }

    private static object Facts(Channel x) => new
    {
        x.Name,
        x.AccountId,
        x.ApplicationIds,
        x.IsEnabled
    };
}
