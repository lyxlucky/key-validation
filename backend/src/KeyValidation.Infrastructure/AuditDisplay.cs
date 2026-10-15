using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KeyValidation.Infrastructure;

// Action classification is shared by ingestion and queries; new modules extend
// this catalog and the focused resource resolver below.
internal static class AuditActions
{
    internal static string Category(string action) => action is "security.login" or "security.login-two-factor" or "security.login-failed" or "security.logout" ? "login" : "operation";
    internal static string Result(string action, string changes)
    {
        if (action is "backup.create" or "backup.verify" or "webhook.retry") return "submitted";
        if (action.EndsWith("-failed", StringComparison.Ordinal)) return "failed";
        if (action.StartsWith("license.", StringComparison.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(changes);
                if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("success", out var outcome) && outcome.ValueKind == JsonValueKind.False) return "failed";
            }
            catch (JsonException) { /* Keep historical unstructured changes intact. */ }
        }
        return "succeeded";
    }
    internal static string? TargetType(string action) => action.Split('.')[0] switch
    {
        "application" or "risk-policy" or "notification-policy" => "application",
        "plan" or "application-plan" => "plan", "batch" => "batch", "license" => "license", "binding" => "binding", "session" => "session",
        "account" or "admin" or "security" => "account", "role" => "role", "risk" => "risk", "webhook" => "webhook", "backup" => "backup",
        "customer" => "customer", "order" => "order", "channel" => "channel", "portal-request" => "portal-request", "api-credential" => "api-credential", "offline-license" => "offline-license", _ => null
    };
}

internal sealed class AuditLabels
{
    internal Dictionary<string, string?> Actors { get; } = new(StringComparer.Ordinal);
    internal Dictionary<Guid, string> Applications { get; } = [];
    internal Dictionary<(string Type, Guid Id), string> Targets { get; } = [];
    internal bool GlobalTargets { get; private init; }

    internal static async Task<AuditLabels> Load(AppDbContext db, IReadOnlyCollection<AdminAuditLog> rows, ApplicationAccessScope? access, bool includeTracked = false, CancellationToken cancellationToken = default)
    {
        var result = new AuditLabels { GlobalTargets = access is null || !access.IsAdministrativeRequest || access.AllApplications && access.ChannelId == null };
        if (rows.Count == 0) return result;
        var actors = rows.Select(x => x.ActorId).Distinct().ToArray();
        foreach (var actor in await db.Users.AsNoTracking().Where(x => actors.Contains(x.Id)).Select(x => new { x.Id, x.UserName }).ToArrayAsync(cancellationToken)) result.Actors[actor.Id] = actor.UserName;
        var apiIds = actors.Where(x => x.StartsWith("api:", StringComparison.Ordinal)).Select(x => Guid.TryParse(x[4..], out var id) ? id : Guid.Empty).ToArray();
        if (apiIds.Length > 0) foreach (var actor in await db.ApiCredentials.AsNoTracking().Where(x => apiIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) result.Actors["api:" + actor.Id] = "API · " + actor.Name;
        foreach (var actor in actors.Where(x => x.StartsWith("portal:", StringComparison.Ordinal))) result.Actors[actor] = "客户自助";
        var appIds = rows.Where(x => x.ApplicationId != null).Select(x => x.ApplicationId!.Value).Concat(Ids("application")).Distinct().ToArray();
        foreach (var application in await db.Applications.AsNoTracking().Where(x => appIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
        { result.Applications[application.Id] = application.Name; result.Targets[("application", application.Id)] = application.Name; }
        if (includeTracked)
        {
            foreach (var application in db.Applications.Local.Where(x => appIds.Contains(x.Id) && (access?.Includes(x.Id) ?? true)))
            { result.Applications[application.Id] = application.Name; result.Targets[("application", application.Id)] = application.Name; }
            foreach (var user in db.Users.Local.Where(x => actors.Contains(x.Id))) result.Actors[user.Id] = user.UserName;
        }
        var ids = Ids("plan");
        if (ids.Length > 0)
        {
            foreach (var row in await db.Plans.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) result.Targets[("plan", row.Id)] = row.Name;
            if (includeTracked) foreach (var row in db.Plans.Local.Where(x => ids.Contains(x.Id) && (result.GlobalTargets || result.Targets.ContainsKey(("plan", x.Id))))) result.Targets[("plan", row.Id)] = row.Name;
        }
        ids = Ids("batch");
        if (ids.Length > 0)
        {
            foreach (var row in await (from batch in db.Batches.AsNoTracking() join plan in db.Plans.AsNoTracking() on batch.PlanId equals plan.Id where ids.Contains(batch.Id) select new { batch.Id, plan.Name, batch.Quantity }).ToArrayAsync(cancellationToken)) result.Targets[("batch", row.Id)] = $"{row.Name} × {row.Quantity}";
            if (includeTracked)
            {
                var pending = db.Batches.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true)).ToArray();
                var planIds = pending.Select(x => x.PlanId).Distinct().ToArray();
                var names = await db.Plans.AsNoTracking().Where(x => planIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
                foreach (var batch in pending) if (names.TryGetValue(batch.PlanId, out var name)) result.Targets[("batch", batch.Id)] = $"{name} × {batch.Quantity}";
            }
        }
        ids = Ids("license");
        if (ids.Length > 0)
        {
            foreach (var row in await db.Licenses.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.KeyHint }).ToArrayAsync(cancellationToken)) result.Targets[("license", row.Id)] = row.KeyHint;
            if (includeTracked) foreach (var row in db.Licenses.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true))) result.Targets[("license", row.Id)] = row.KeyHint;
        }
        ids = Ids("binding");
        if (ids.Length > 0) foreach (var row in await (from binding in db.Bindings.AsNoTracking() join license in db.Licenses.AsNoTracking() on binding.LicenseId equals license.Id where ids.Contains(binding.Id) select new { binding.Id, license.KeyHint }).ToArrayAsync(cancellationToken)) result.Targets[("binding", row.Id)] = row.KeyHint;
        ids = Ids("session");
        if (ids.Length > 0) foreach (var row in await (from session in db.Sessions.AsNoTracking() join binding in db.Bindings.AsNoTracking() on session.BindingId equals binding.Id join license in db.Licenses.AsNoTracking() on binding.LicenseId equals license.Id where ids.Contains(session.Id) select new { session.Id, license.KeyHint }).ToArrayAsync(cancellationToken)) result.Targets[("session", row.Id)] = row.KeyHint;
        ids = Ids("offline-license");
        if (ids.Length > 0)
        {
            foreach (var row in await (from record in db.OfflineLicenses.AsNoTracking() join license in db.Licenses.AsNoTracking() on record.LicenseId equals license.Id where ids.Contains(record.Id) select new { record.Id, license.KeyHint, record.Serial }).ToArrayAsync(cancellationToken)) result.Targets[("offline-license", row.Id)] = $"{row.KeyHint} · {row.Serial.ToString("N")[..8]}";
            if (includeTracked)
            {
                var pending = db.OfflineLicenses.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true) && (access?.IncludesChannel(x.ChannelId) ?? true)).ToArray();
                var licenseIds = pending.Select(x => x.LicenseId).Distinct().ToArray();
                var hints = await db.Licenses.AsNoTracking().Where(x => licenseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.KeyHint, cancellationToken);
                foreach (var row in pending) if (hints.TryGetValue(row.LicenseId, out var hint)) result.Targets[("offline-license", row.Id)] = $"{hint} · {row.Serial.ToString("N")[..8]}";
            }
        }
        ids = Ids("risk");
        if (ids.Length > 0) foreach (var row in await (from risk in db.RiskEvents.AsNoTracking() join license in db.Licenses.AsNoTracking() on risk.LicenseId equals license.Id where ids.Contains(risk.Id) select new { risk.Id, risk.Rule, license.KeyHint }).ToArrayAsync(cancellationToken)) result.Targets[("risk", row.Id)] = $"{row.KeyHint} · {row.Rule}";
        ids = Ids("customer");
        // Personal labels are resolved only for reads, through the current scope.
        // Ingestion stores anonymous references so audit history never retains names.
        if (ids.Length > 0 && !includeTracked)
            foreach (var row in await db.Customers.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name, x.AnonymizedAt }).ToArrayAsync(cancellationToken))
                result.Targets[("customer", row.Id)] = row.AnonymizedAt is null && !string.IsNullOrWhiteSpace(row.Name) ? row.Name : AnonymousReference("customer", row.Id);
        ids = Ids("order");
        if (ids.Length > 0)
            foreach (var row in await db.Orders.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.PlanName, x.Type, x.Quantity }).ToArrayAsync(cancellationToken))
                result.Targets[("order", row.Id)] = $"{row.PlanName} · {(row.Type == "renewal" ? "续期" : "发卡")} × {row.Quantity}";
        ids = Ids("portal-request");
        if (ids.Length > 0 && !includeTracked)
            foreach (var row in await (from request in db.PortalRequests.AsNoTracking() join customer in db.Customers.AsNoTracking() on request.CustomerId equals customer.Id where ids.Contains(request.Id) select new { request.Id, request.Type, customer.Name, customer.AnonymizedAt }).ToArrayAsync(cancellationToken))
                result.Targets[("portal-request", row.Id)] = row.AnonymizedAt is null && !string.IsNullOrWhiteSpace(row.Name)
                    ? $"{row.Name} · {(row.Type == "rebinding" ? "换绑申请" : "续期申请")}" : AnonymousReference("portal-request", row.Id);
        ids = Ids("channel");
        if (ids.Length > 0) foreach (var row in await db.Channels.AsNoTracking().Where(x => ids.Contains(x.Id) && (access == null || access.AllApplications || x.ApplicationIds.All(id => access.ApplicationIds.Contains(id)))).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) result.Targets[("channel", row.Id)] = row.Name;
        ids = Ids("api-credential");
        if (ids.Length > 0) foreach (var row in await db.ApiCredentials.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) result.Targets[("api-credential", row.Id)] = row.Name;
        ids = Ids("webhook");
        if (ids.Length > 0)
        {
            foreach (var row in await db.WebhookSubscriptions.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) result.Targets[("webhook", row.Id)] = row.Name;
            foreach (var row in await (from delivery in db.WebhookDeliveries.AsNoTracking() join subscription in db.WebhookSubscriptions.AsNoTracking() on delivery.SubscriptionId equals subscription.Id where ids.Contains(delivery.Id) select new { delivery.Id, subscription.Name }).ToArrayAsync(cancellationToken)) result.Targets[("webhook", row.Id)] = row.Name;
            if (includeTracked) foreach (var row in db.WebhookSubscriptions.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true))) result.Targets[("webhook", row.Id)] = row.Name;
        }
        if (result.GlobalTargets)
        {
            var accountIds = Ids("account").Select(x => x.ToString()).ToArray();
            if (accountIds.Length > 0) foreach (var row in await db.Users.AsNoTracking().Where(x => accountIds.Contains(x.Id)).Select(x => new { x.Id, x.UserName }).ToArrayAsync(cancellationToken)) if (Guid.TryParse(row.Id, out var id) && row.UserName is not null) result.Targets[("account", id)] = row.UserName;
            var roleIds = Ids("role").Select(x => x.ToString()).ToArray();
            if (roleIds.Length > 0) foreach (var row in await db.Roles.AsNoTracking().Where(x => roleIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken)) if (Guid.TryParse(row.Id, out var id) && row.Name is not null) result.Targets[("role", id)] = row.Name;
            ids = Ids("backup");
            if (ids.Length > 0) foreach (var row in await db.BackupJobs.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.CreatedAt }).ToArrayAsync(cancellationToken)) result.Targets[("backup", row.Id)] = row.CreatedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
        }
        return result;
        Guid[] Ids(string type) => rows.Where(x => (x.TargetType ?? AuditActions.TargetType(x.Action)) == type).Select(x => x.TargetId).Distinct().ToArray();
    }

    internal AuditDto Dto(AdminAuditLog row)
    {
        var type = row.TargetType ?? AuditActions.TargetType(row.Action);
        string? targetName = null;
        if (type is not null && Targets.TryGetValue((type, row.TargetId), out var resolved))
            targetName = type is "customer" or "order" or "portal-request" ? resolved : row.TargetName ?? resolved;
        else if (GlobalTargets)
            targetName = type is "customer" or "portal-request" ? AnonymousReference(type, row.TargetId) : row.TargetName;
        var applicationName = row.ApplicationId is { } app && Applications.TryGetValue(app, out var name) ? row.ApplicationName ?? name : null;
        return new(row.Id, row.ActorId, row.Action, row.TargetId, row.Changes, row.CreatedAt, row.TraceId,
            row.Category, row.Result, row.ActorName ?? Actors.GetValueOrDefault(row.ActorId), type, targetName,
            row.ApplicationId, applicationName, row.SourceIp, row.Method, row.ErrorCategory);
    }

    internal static string AnonymousReference(string type, Guid id) => (type == "customer" ? "客户 " : "自助申请 ") + id.ToString("N")[..8];
}

public sealed class AuditLogService(AppDbContext db, ApplicationAccessScope access)
{
    public async Task<PagedResult<AuditDto>> List(int page, int pageSize, string? actorId, Guid? targetId, string? action, DateTimeOffset? from, DateTimeOffset? to, string? category = null, string? actorName = null)
    {
        access.Require(ApplicationPermissions.ReadAudit); ReliabilityRules.Page(page, pageSize);
        if (actorId?.Length > 450 || action?.Length > 80 || actorName?.Length > 128 || from > to || category is not null and not "operation" and not "login") throw new RuleException("VALIDATION_ERROR", 400);
        var query = db.AuditLogs.AsNoTracking();
        if (actorId is not null) query = query.Where(x => x.ActorId == actorId);
        if (targetId is not null) query = query.Where(x => x.TargetId == targetId);
        if (action is not null) query = query.Where(x => x.Action == action);
        if (from is not null) query = query.Where(x => x.CreatedAt >= from.Value.ToUniversalTime());
        if (to is not null) query = query.Where(x => x.CreatedAt < to.Value.ToUniversalTime());
        if (category is not null) query = query.Where(x => x.Category == category);
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

public sealed partial class AppDbContext
{
    private static void ConfigureAuditDisplay(ModelBuilder b)
    {
        b.Entity<AdminAuditLog>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(16).HasDefaultValue("operation"); e.Property(x => x.Result).HasMaxLength(16).HasDefaultValue("succeeded");
            e.Property(x => x.ActorName).HasMaxLength(256); e.Property(x => x.TargetType).HasMaxLength(32); e.Property(x => x.TargetName).HasMaxLength(256);
            e.Property(x => x.ApplicationName).HasMaxLength(120); e.Property(x => x.SourceIp).HasMaxLength(64); e.Property(x => x.Method).HasMaxLength(32); e.Property(x => x.ErrorCategory).HasMaxLength(40);
            e.HasIndex(x => new { x.Category, x.CreatedAt });
        });
    }

    private async Task PopulateAuditDisplay(CancellationToken cancellationToken)
    {
        var rows = ChangeTracker.Entries<AdminAuditLog>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
        if (rows.Length == 0) return;
        var labels = await AuditLabels.Load(this, rows, access, includeTracked: true, cancellationToken);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Category)) row.Category = AuditActions.Category(row.Action);
            if (string.IsNullOrEmpty(row.Result)) row.Result = AuditActions.Result(row.Action, row.Changes);
            row.TargetType ??= AuditActions.TargetType(row.Action); row.ActorName ??= labels.Actors.GetValueOrDefault(row.ActorId);
            if (row.TargetType is "customer" or "portal-request") row.TargetName = AuditLabels.AnonymousReference(row.TargetType, row.TargetId);
            else if (row.TargetType is { } type) row.TargetName ??= labels.Targets.GetValueOrDefault((type, row.TargetId));
            if (row.ApplicationId is { } id) row.ApplicationName ??= labels.Applications.GetValueOrDefault(id);
        }
    }
}
