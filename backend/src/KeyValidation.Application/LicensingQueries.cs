using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed partial class LicensingService
{
    private static ApplicationDto AppDto(SoftwareApplication a) => new(a.Id, a.Code, a.Name, a.DisabledAt, a.CreatedAt);
    private static BatchDto BatchDtoOf(LicenseBatch b) => new(b.Id, b.ApplicationId, b.PlanId, b.Quantity, b.CreatedBy, b.CreatedAt, b.Note, b.RequestId);
    private LicenseDto LicenseDtoOf(License l) => LicenseDtoAt(l, Now);
    private static LicenseDto LicenseDtoAt(License l, DateTimeOffset now) => new(l.Id, l.ApplicationId, l.BatchId, l.KeyHint, l.DurationSeconds, l.MaxDevices, l.Status(now), l.CreatedAt, l.ActivatedAt, l.ExpiresAt, l.DisabledAt, l.Note, l.Tags, l.MaxConcurrentSessions, l.ActivateBefore, l.FrozenAt, l.FrozenRemainingSeconds, l.Features, l.IsTrial);
    private static void ValidatePage(int page, int pageSize) => Require(page is >= 1 and <= 1000000 && pageSize is >= 1 and <= 100);
    private static async Task<PagedResult<TDto>> Page<T, TDto>(IQueryable<T> query, int page, int size, Func<T, TDto> map)
    {
        ValidatePage(page, size); var count = await query.CountAsync(); var rows = await query.Skip((page - 1) * size).Take(size).ToListAsync(); return new(rows.Select(map).ToArray(), count, page, size);
    }
    public Task<PagedResult<ApplicationDto>> Applications(int page, int size)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        return Page(db.Applications.AsNoTracking().OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), page, size, AppDto);
    }
    public async Task<PagedResult<PlanDto>> Plans(Guid appId, int page, int size)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        if (!await db.Applications.AnyAsync(x => x.Id == appId)) throw new RuleException("NOT_FOUND", 404);
        ValidatePage(page, size);
        var query = planQueries.Scoped().Where(x => x.ApplicationId == appId);
        var count = await query.CountAsync();
        var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToArrayAsync();
        return new PagedResult<PlanDto>(items, count, page, size);
    }
    public Task<PagedResult<BatchDto>> Batches(int page, int size, Guid? app, Guid? requestId, Actor actor, Guid? channelId = null)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        var q = db.Batches.AsNoTracking().AsQueryable();
        if (app is not null) q = q.Where(x => x.ApplicationId == app);
        if (channelId is not null) q = q.Where(x => x.ChannelId == channelId);
        if (requestId is not null) q = q.Where(x => x.RequestId == requestId && x.CreatedBy == actor.Id);
        return Page(q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), page, size, BatchDtoOf);
    }
    public Task<PagedResult<LicenseDto>> Licenses(int page, int size, Guid? app, Guid? batch, string? status, DateTimeOffset? from, DateTimeOffset? to, string? note = null, string? tag = null, Guid? channelId = null)
        => QueryLicenses(new(page, size, app, batch, status, from, to, Note: note, Tag: tag, ChannelId: channelId));

    public async Task<PagedResult<LicenseDto>> QueryLicenses(LicenseQuery request)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        ValidatePage(request.Page, request.PageSize);
        Require((request.Note?.Length ?? 0) <= 500 && (request.Tag?.Length ?? 0) <= 32 && (request.DeviceId?.Length ?? 0) <= 256);
        ValidateRange(request.From, request.To); ValidateRange(request.ActivatedFrom, request.ActivatedTo); ValidateRange(request.ExpiresFrom, request.ExpiresTo);
        var q = db.Licenses.AsNoTracking().AsQueryable(); var now = Now;
        if (request.ApplicationId is not null) q = q.Where(x => x.ApplicationId == request.ApplicationId);
        if (request.ChannelId is not null) q = q.Where(x => x.ChannelId == request.ChannelId);
        if (request.BatchId is not null) q = q.Where(x => x.BatchId == request.BatchId);
        if (request.From is not null) { var from = request.From.Value.ToUniversalTime(); q = q.Where(x => x.CreatedAt >= from); }
        if (request.To is not null) { var to = request.To.Value.ToUniversalTime(); q = q.Where(x => x.CreatedAt < to); }
        if (request.ActivatedFrom is not null) { var from = request.ActivatedFrom.Value.ToUniversalTime(); q = q.Where(x => x.ActivatedAt >= from); }
        if (request.ActivatedTo is not null) { var to = request.ActivatedTo.Value.ToUniversalTime(); q = q.Where(x => x.ActivatedAt < to); }
        if (request.ExpiresFrom is not null) { var from = request.ExpiresFrom.Value.ToUniversalTime(); q = q.Where(x => x.ExpiresAt >= from); }
        if (request.ExpiresTo is not null) { var to = request.ExpiresTo.Value.ToUniversalTime(); q = q.Where(x => x.ExpiresAt < to); }
        if (!string.IsNullOrWhiteSpace(request.Note)) { var text = request.Note.Trim(); q = q.Where(x => x.Note != null && x.Note.Contains(text)); }
        if (!string.IsNullOrWhiteSpace(request.Tag)) { var text = request.Tag.Trim(); q = q.Where(x => x.Tags.Contains(text)); }
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            Require(request.ApplicationId != null);
            if (!await db.Applications.AnyAsync(x => x.Id == request.ApplicationId)) throw new RuleException("NOT_FOUND", 404);
            var digest = secrets.DigestDevice(request.ApplicationId!.Value, request.DeviceId);
            var matchingLicenses = db.Bindings.Where(x => x.DeviceDigest == digest).Select(x => x.LicenseId);
            q = q.Where(x => matchingLicenses.Contains(x.Id));
        }
        q = request.Status switch
        {
            null or "" => q,
            "disabled" => q.Where(x => x.DisabledAt != null),
            "frozen" => q.Where(x => x.DisabledAt == null && x.FrozenAt != null),
            "activation_expired" => q.Where(x => x.DisabledAt == null && x.ActivatedAt == null && x.ActivateBefore <= now),
            "unused" => q.Where(x => x.DisabledAt == null && x.ActivatedAt == null && (x.ActivateBefore == null || x.ActivateBefore > now)),
            "active" => q.Where(x => x.DisabledAt == null && x.FrozenAt == null && x.ActivatedAt != null && x.ExpiresAt > now),
            "expired" => q.Where(x => x.DisabledAt == null && x.FrozenAt == null && x.ActivatedAt != null && x.ExpiresAt <= now),
            _ => throw new RuleException("VALIDATION_ERROR", 400)
        };
        return await Page(q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), request.Page, request.PageSize, LicenseDtoOf);
    }
    private static void ValidateRange(DateTimeOffset? from, DateTimeOffset? to) => Require(from is null || to is null || from <= to);
    public async Task<ExpiringLicensesDto> ExpiringLicenses(int page, int size, int withinDays, Guid? app, Guid? channelId = null)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        ValidatePage(page, size); Require(withinDays is 3 or 7 or 30);
        var now = Now; var through = now.AddDays(withinDays);
        var enabledApps = db.Applications.Where(x => x.DisabledAt == null).Select(x => x.Id);
        var query = db.Licenses.AsNoTracking().Where(x => x.ActivatedAt != null && x.DisabledAt == null && x.FrozenAt == null && enabledApps.Contains(x.ApplicationId) && x.ExpiresAt > now && x.ExpiresAt <= through);
        if (app is not null) query = query.Where(x => x.ApplicationId == app);
        if (channelId is not null) query = query.Where(x => x.ChannelId == channelId);
        var total = await query.CountAsync();
        var rows = await query.OrderBy(x => x.ExpiresAt).ThenBy(x => x.Id).Skip((page - 1) * size).Take(size).ToListAsync();
        return new(rows.Select(x => LicenseDtoAt(x, now)).ToArray(), total, page, size, now, withinDays);
    }
    private IQueryable<BatchOverviewDto> BatchOverviewQuery(IQueryable<LicenseBatch> batches) =>
        from batch in batches
        join application in db.Applications.AsNoTracking() on batch.ApplicationId equals application.Id
        join plan in db.Plans.AsNoTracking() on batch.PlanId equals plan.Id
        orderby batch.CreatedAt descending, batch.Id
        select new BatchOverviewDto(batch.Id, batch.ApplicationId, batch.PlanId, batch.Quantity, batch.CreatedBy, batch.CreatedAt, batch.Note, batch.RequestId,
            application.Name, plan.Name, null,
            db.Licenses.Count(x => x.BatchId == batch.Id && x.ActivatedAt != null),
            db.Licenses.Count(x => x.BatchId == batch.Id && x.DisabledAt != null));
    public Task<PagedResult<BatchOverviewDto>> BatchOverview(int page, int size, Guid? app, Guid? plan, string? createdBy, string? search, Guid? channelId = null)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        Require((createdBy?.Length ?? 0) <= 450 && (search?.Length ?? 0) <= 500);
        var query = db.Batches.AsNoTracking().AsQueryable();
        if (channelId is not null) query = query.Where(x => x.ChannelId == channelId);
        if (app is not null) query = query.Where(x => x.ApplicationId == app);
        if (plan is not null) query = query.Where(x => x.PlanId == plan);
        if (!string.IsNullOrWhiteSpace(createdBy)) query = query.Where(x => x.CreatedBy == createdBy);
        if (!string.IsNullOrWhiteSpace(search)) { var text = search.Trim(); query = query.Where(x => x.Note != null && x.Note.Contains(text)); }
        return Page(BatchOverviewQuery(query), page, size, x => x);
    }
    public async Task<BatchOverviewDto> BatchOverviewDetail(Guid id)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        return await BatchOverviewQuery(db.Batches.AsNoTracking().Where(x => x.Id == id)).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
    }
    public async Task<LicenseDetail> Detail(Guid id)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        var license = await db.Licenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        var bindings = await db.Bindings.AsNoTracking().Where(x => x.LicenseId == id).OrderByDescending(x => x.BoundAt).ToListAsync();
        var ids = bindings.Select(x => x.Id).ToArray();
        var sessions = await db.Sessions.AsNoTracking().Where(x => ids.Contains(x.BindingId)).OrderByDescending(x => x.CreatedAt).ToListAsync();
        return new(LicenseDtoOf(license), bindings.Select(b => new BindingDto(b.Id, b.DeviceLabel, b.BoundAt, b.ReleasedAt, sessions.Where(s => s.BindingId == b.Id).Select(s => new SessionDto(s.Id, s.CreatedAt, s.AbsoluteExpiresAt, s.LeaseExpiresAt, s.LastSeenAt, s.RevokedAt)).ToArray())).ToArray());
    }
    public async Task<LicenseDetail> Search(string key)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        var digest = secrets.DigestLicense(key).Digest;
        var id = await db.Licenses.Where(x => x.KeyDigest == digest).Select(x => (Guid?)x.Id).SingleOrDefaultAsync() ?? throw new RuleException("NOT_FOUND", 404);
        return await Detail(id);
    }
    public Task<PagedResult<AuditDto>> Audits(int page, int size, string? actorId, Guid? targetId, string? action, DateTimeOffset? from, DateTimeOffset? to)
    {
        Demand(ApplicationPermissions.ReadAudit);
        ValidateRange(from, to);
        Require((actorId?.Length ?? 0) <= 450 && (action?.Length ?? 0) <= 80);
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (actorId is not null) q = q.Where(x => x.ActorId == actorId);
        if (targetId is not null) q = q.Where(x => x.TargetId == targetId);
        if (action is not null) q = q.Where(x => x.Action == action);
        if (from is not null) q = q.Where(x => x.CreatedAt >= from);
        if (to is not null) q = q.Where(x => x.CreatedAt < to);
        return Page(q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id), page, size, a => new AuditDto(a.Id, a.ActorId, a.Action, a.TargetId, a.Changes, a.CreatedAt, a.TraceId,
            a.Category, a.Result, a.ActorName, a.TargetType, a.TargetName, a.ApplicationId, a.ApplicationName, a.SourceIp, a.Method, a.ErrorCategory));
    }
    public async Task<DashboardDto> Dashboard(Guid? applicationId = null, Guid? channelId = null)
    {
        Demand(ApplicationPermissions.ReadReports);
        var now = Now;
        var activeApps = db.Applications.Where(x => x.DisabledAt == null).Select(x => x.Id);
        var all = db.Licenses.Where(x => (applicationId == null || x.ApplicationId == applicationId) && (channelId == null || x.ChannelId == channelId));
        var valid = all.Where(x => activeApps.Contains(x.ApplicationId) && x.DisabledAt == null && x.FrozenAt == null && x.ExpiresAt > now);
        var validIds = valid.Select(x => x.Id); var bindings = db.Bindings.Where(x => x.ReleasedAt == null && validIds.Contains(x.LicenseId)).Select(x => x.Id);
        return new(await all.CountAsync(), await all.CountAsync(x => x.ActivatedAt != null), await valid.CountAsync(), await db.Sessions.CountAsync(x => bindings.Contains(x.BindingId) && x.RevokedAt == null && x.LeaseExpiresAt > now && x.AbsoluteExpiresAt > now));
    }
}
