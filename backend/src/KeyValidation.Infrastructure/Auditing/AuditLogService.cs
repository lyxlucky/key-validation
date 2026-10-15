using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KeyValidation.Infrastructure;
public sealed class AuditLogService(AppDbContext db, ApplicationAccessScope access)
{
    public async Task<PagedResult<AuditDto>> List(int page, int pageSize, string? actorId, Guid? targetId, string? action, DateTimeOffset? from, DateTimeOffset? to, string? category = null, string? actorName = null)
    {
        access.Require(ApplicationPermissions.ReadAudit);
        ReliabilityRules.Page(page, pageSize);
        if (actorId?.Length > 450 || action?.Length > 80 || actorName?.Length > 128 || from > to || category is not null and not "operation" and not "login")
            throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.AuditLogs.AsNoTracking();
        if (actorId is not null)
            query = query.Where(x => x.ActorId == actorId);
        if (targetId is not null)
            query = query.Where(x => x.TargetId == targetId);
        if (action is not null)
            query = query.Where(x => x.Action == action);
        if (from is not null)
            query = query.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());
        if (to is not null)
            query = query.Where(x => x.CreatedAt < to.Value.ToUniversalTime());
        if (category is not null)
            query = query.Where(x => x.Category == category);
        if (!string.IsNullOrWhiteSpace(actorName))
        {
            var normalized = actorName.Trim().ToUpperInvariant();
            query = query.Where(x => x.ActorName != null && x.ActorName.ToUpper().Contains(normalized) || x.ActorName == null && db.Users.Any(u => u.Id == x.ActorId && u.NormalizedUserName != null && u.NormalizedUserName.Contains(normalized)));
        }

        var count = await query.CountAsync();
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        var labels = await AuditLabels.Load(db, rows, access);
        return new(rows.Select(labels.Dto).ToArray(), count, page, pageSize);
    }
}
