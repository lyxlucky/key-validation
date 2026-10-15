using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class PlanCatalogService(ILicensingData db, ApplicationAccessScope access,
    TimeProvider clock, PlanCatalogQueries queries)
{
    private DateTimeOffset Now
    {
        get
        {
            var now = clock.GetUtcNow();
            return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero);
        }
    }

    private static void ValidatePage(int page, int size)
    {
        if (page is < 1 or > 1000000 || size is < 1 or > 100)
            throw new RuleException("VALIDATION_ERROR", 400);
    }

    private IQueryable<GlobalPlanDto> Global() => db.Plans.AsNoTracking()
        .Select(p => new GlobalPlanDto(p.Id, p.Name, p.DurationSeconds,
            p.MaxDevices, p.IsEnabled, p.CreatedAt, p.MaxConcurrentSessions,
            p.ActivationValidityDays, p.Features,
            db.ApplicationPlans.Count(r => r.PlanId == p.Id))
        { Id = p.Id, Name = p.Name, IsEnabled = p.IsEnabled, CreatedAt = p.CreatedAt });

    private IQueryable<ApplicationPlanDto> ReferenceRows() =>
        from r in db.ApplicationPlans.AsNoTracking()
        join p in db.Plans.AsNoTracking() on r.PlanId equals p.Id
        join a in db.Applications.AsNoTracking() on r.ApplicationId equals a.Id
        select new ApplicationPlanDto(a.Id, p.Id, p.Name, p.DurationSeconds,
            p.MaxDevices, p.MaxConcurrentSessions, p.ActivationValidityDays,
            p.Features, p.IsEnabled, r.IsEnabled,
            a.DisabledAt == null && p.IsEnabled && r.IsEnabled, r.CreatedAt)
        { ApplicationId = a.Id, PlanId = p.Id, Name = p.Name };

    public async Task<PagedResult<GlobalPlanDto>> List(int page, int size,
        string? search, bool? isEnabled)
    {
        access.RequireGlobalPlanManagement();
        ValidatePage(page, size);
        search = search?.Trim();
        if (search?.Length > 120) throw new RuleException("VALIDATION_ERROR", 400);
        var q = Global();
        if (!string.IsNullOrEmpty(search)) q = q.Where(x => x.Name.Contains(search));
        if (isEnabled is { } state) q = q.Where(x => x.IsEnabled == state);
        var total = await q.CountAsync();
        var items = await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip((page - 1) * size).Take(size).ToArrayAsync();
        return new(items, total, page, size);
    }

    public async Task<GlobalPlanDto> Get(Guid id)
    {
        access.RequireGlobalPlanManagement();
        return await Global().SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RuleException("NOT_FOUND", 404);
    }

    public async Task<PagedResult<ApplicationPlanDto>> References(
        Guid applicationId, int page, int size)
    {
        access.Require(ApplicationPermissions.ReadLicenses);
        ValidatePage(page, size);
        if (!await db.Applications.AnyAsync(x => x.Id == applicationId))
            throw new RuleException("NOT_FOUND", 404);
        var q = ReferenceRows().Where(x => x.ApplicationId == applicationId);
        var total = await q.CountAsync();
        var items = await q.OrderBy(x => x.Name).ThenBy(x => x.PlanId)
            .Skip((page - 1) * size).Take(size).ToArrayAsync();
        return new(items, total, page, size);
    }

    public async Task<IReadOnlyList<PlanReferenceDto>> Applications(Guid planId)
    {
        await Get(planId);
        return await (from r in db.ApplicationPlans.AsNoTracking()
                      join a in db.Applications.AsNoTracking() on r.ApplicationId equals a.Id
                      where r.PlanId == planId
                      orderby a.Name, a.Id
                      select new PlanReferenceDto(a.Id, a.Name, r.IsEnabled, r.CreatedAt)).ToArrayAsync();
    }

    private static (string Name, int Sessions, string[] Features) Normalize(CreatePlan request)
    {
        var name = request.Name?.Trim();
        if (name is not { Length: >= 1 and <= 120 } ||
            request.DurationSeconds is < 1 or > 315360000 ||
            request.MaxDevices is < 1 or > 100)
            throw new RuleException("VALIDATION_ERROR", 400);
        var sessions = request.MaxConcurrentSessions ?? request.MaxDevices;
        AuthorizationPolicies.Validate(sessions, request.ActivationValidityDays);
        return (name, sessions, AuthorizationPolicies.NormalizeFeatures(request.Features));
    }

    private LicensePlan NewPlan(CreatePlan request)
    {
        var values = Normalize(request);
        var plan = new LicensePlan
        {
            Name = values.Name,
            DurationSeconds = request.DurationSeconds,
            MaxDevices = request.MaxDevices,
            MaxConcurrentSessions = values.Sessions,
            ActivationValidityDays = request.ActivationValidityDays,
            Features = values.Features,
            IsEnabled = true,
            CreatedAt = Now
        };
        db.Plans.Add(plan);
        return plan;
    }

    private static object Facts(LicensePlan plan) => new
    {
        name = plan.Name,
        durationSeconds = plan.DurationSeconds,
        maxDevices = plan.MaxDevices,
        maxConcurrentSessions = plan.MaxConcurrentSessions,
        activationValidityDays = plan.ActivationValidityDays,
        features = plan.Features.ToArray(),
        isEnabled = plan.IsEnabled
    };

    private void Audit(Actor actor, string action, LicensePlan plan,
        Guid? applicationId, object? before, object after)
    {
        db.AuditLogs.Add(new AdminAuditLog
        {
            ActorId = actor.Id,
            Action = action,
            TargetId = plan.Id,
            ApplicationId = applicationId,
            TargetType = "plan",
            TargetName = plan.Name,
            Changes = JsonSerializer.Serialize(new { before, after }),
            CreatedAt = Now,
            TraceId = actor.TraceId
        });
    }

    private async Task LockApplication(Guid id)
    {
        if (id == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Applications" WHERE "Id" = {id} FOR SHARE""");
        if (!await db.Applications.AnyAsync(x => x.Id == id))
            throw new RuleException("NOT_FOUND", 404);
    }
    public async Task<GlobalPlanDto> Create(CreatePlan request, Actor actor)
    {
        access.RequireGlobalPlanManagement(write: true);
        await using var tx = await db.Database.BeginTransactionAsync();
        var plan = NewPlan(request);
        Audit(actor, "plan.create", plan, null, null, Facts(plan));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(plan.Id);
    }

    public async Task<GlobalPlanDto> Update(Guid id, UpdatePlan request, Actor actor)
    {
        access.RequireGlobalPlanManagement(write: true);
        if (id == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
        var values = Normalize(new CreatePlan(request.Name, request.DurationSeconds,
            request.MaxDevices, request.MaxConcurrentSessions,
            request.ActivationValidityDays, request.Features));
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Plans" WHERE "Id" = {id} FOR UPDATE""");
        var plan = await db.Plans.SingleOrDefaultAsync(x => x.Id == id)
            ?? throw new RuleException("NOT_FOUND", 404);
        var before = Facts(plan);
        plan.Name = values.Name;
        plan.DurationSeconds = request.DurationSeconds;
        plan.MaxDevices = request.MaxDevices;
        plan.MaxConcurrentSessions = values.Sessions;
        plan.ActivationValidityDays = request.ActivationValidityDays;
        plan.Features = values.Features;
        plan.IsEnabled = request.IsEnabled;
        Audit(actor, "plan.update", plan, null, before, Facts(plan));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await Get(id);
    }

    public async Task<PlanDto> CreateForApplication(Guid applicationId,
        CreatePlan request, Actor actor)
    {
        access.RequireGlobalPlanManagement(write: true);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockApplication(applicationId);
        var plan = NewPlan(request);
        db.ApplicationPlans.Add(new ApplicationPlan
        {
            ApplicationId = applicationId,
            PlanId = plan.Id,
            IsEnabled = true,
            CreatedAt = plan.CreatedAt
        });
        Audit(actor, "plan.create", plan, null, null, Facts(plan));
        Audit(actor, "application-plan.bind", plan, applicationId, null, new { isEnabled = true });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await queries.Scoped().SingleAsync(
            x => x.ApplicationId == applicationId && x.Id == plan.Id);
    }
    public async Task<ApplicationPlanDto> SetReference(Guid applicationId, Guid planId,
        SetPlanReference request, Actor actor)
    {
        access.RequireGlobalPlanManagement(write: true);
        if (planId == Guid.Empty) throw new RuleException("VALIDATION_ERROR", 400);
        await using var tx = await db.Database.BeginTransactionAsync();
        await LockApplication(applicationId);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "Id" FROM "Plans" WHERE "Id" = {planId} FOR SHARE""");
        var plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(x => x.Id == planId)
            ?? throw new RuleException("NOT_FOUND", 404);
        var now = Now;
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
        INSERT INTO "ApplicationPlans" ("ApplicationId", "PlanId", "IsEnabled", "CreatedAt")
        VALUES ({applicationId}, {planId}, {request.IsEnabled}, {now})
        ON CONFLICT ("ApplicationId", "PlanId") DO NOTHING
        """);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""SELECT "PlanId" FROM "ApplicationPlans" WHERE "ApplicationId" = {applicationId} AND "PlanId" = {planId} FOR UPDATE""");
        var reference = await db.ApplicationPlans.SingleAsync(
            x => x.ApplicationId == applicationId && x.PlanId == planId);
        var before = reference.IsEnabled;
        reference.IsEnabled = request.IsEnabled;
        if (inserted == 1)
            Audit(actor, "application-plan.bind", plan, applicationId, null,
                new { isEnabled = request.IsEnabled });
        else if (before != request.IsEnabled)
            Audit(actor, "application-plan.update", plan, applicationId,
                new { isEnabled = before }, new { isEnabled = request.IsEnabled });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await ReferenceRows().SingleAsync(
            x => x.ApplicationId == applicationId && x.PlanId == planId);
    }
}
