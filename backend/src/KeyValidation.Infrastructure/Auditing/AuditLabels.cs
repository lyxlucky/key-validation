using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KeyValidation.Infrastructure;
internal sealed class AuditLabels
{
    internal Dictionary<string, string?> Actors { get; } = new(StringComparer.Ordinal);
    internal Dictionary<Guid, string> Applications { get; } = [];
    internal Dictionary<(string Type, Guid Id), string> Targets { get; } = [];
    internal bool GlobalTargets { get; private init; }

    internal static async Task<AuditLabels> Load(AppDbContext db, IReadOnlyCollection<AdminAuditLog> rows, ApplicationAccessScope? access, bool includeTracked = false, CancellationToken cancellationToken = default)
    {
        var result = new AuditLabels
        {
            GlobalTargets = access is null || !access.IsAdministrativeRequest || access.AllApplications && access.ChannelId == null
        };
        if (rows.Count == 0)
            return result;
        var actors = rows.Select(x => x.ActorId).Distinct().ToArray();
        foreach (var actor in await db.Users.AsNoTracking().Where(x => actors.Contains(x.Id)).Select(x => new { x.Id, x.UserName }).ToArrayAsync(cancellationToken))
            result.Actors[actor.Id] = actor.UserName;
        var apiIds = actors.Where(x => x.StartsWith("api:", StringComparison.Ordinal)).Select(x => Guid.TryParse(x[4..], out var id) ? id : Guid.Empty).ToArray();
        if (apiIds.Length > 0)
            foreach (var actor in await db.ApiCredentials.AsNoTracking().Where(x => apiIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                result.Actors["api:" + actor.Id] = "API · " + actor.Name;
        foreach (var actor in actors.Where(x => x.StartsWith("portal:", StringComparison.Ordinal)))
            result.Actors[actor] = "客户自助";
        var appIds = rows.Where(x => x.ApplicationId != null).Select(x => x.ApplicationId!.Value).Concat(Ids("application")).Distinct().ToArray();
        foreach (var application in await db.Applications.AsNoTracking().Where(x => appIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
        {
            result.Applications[application.Id] = application.Name;
            result.Targets[("application", application.Id)] = application.Name;
        }

        if (includeTracked)
        {
            foreach (var application in db.Applications.Local.Where(x => appIds.Contains(x.Id) && (access?.Includes(x.Id) ?? true)))
            {
                result.Applications[application.Id] = application.Name;
                result.Targets[("application", application.Id)] = application.Name;
            }

            foreach (var user in db.Users.Local.Where(x => actors.Contains(x.Id)))
                result.Actors[user.Id] = user.UserName;
        }

        var ids = Ids("plan");
        if (ids.Length > 0)
        {
            foreach (var row in await db.Plans.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                result.Targets[("plan", row.Id)] = row.Name;
            if (includeTracked)
                foreach (var row in db.Plans.Local.Where(x => ids.Contains(x.Id) && (result.GlobalTargets || result.Targets.ContainsKey(("plan", x.Id)))))
                    result.Targets[("plan", row.Id)] = row.Name;
        }

        ids = Ids("batch");
        if (ids.Length > 0)
        {
            foreach (var row in await (
                from batch in db.Batches.AsNoTracking()join plan in db.Plans.AsNoTracking()on batch.PlanId equals plan.Id
                where ids.Contains(batch.Id)select new
                {
                    batch.Id,
                    plan.Name,
                    batch.Quantity
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("batch", row.Id)] = $"{row.Name} × {row.Quantity}";
            if (includeTracked)
            {
                var pending = db.Batches.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true)).ToArray();
                var planIds = pending.Select(x => x.PlanId).Distinct().ToArray();
                var names = await db.Plans.AsNoTracking().Where(x => planIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);
                foreach (var batch in pending)
                    if (names.TryGetValue(batch.PlanId, out var name))
                        result.Targets[("batch", batch.Id)] = $"{name} × {batch.Quantity}";
            }
        }

        ids = Ids("license");
        if (ids.Length > 0)
        {
            foreach (var row in await db.Licenses.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.KeyHint }).ToArrayAsync(cancellationToken))
                result.Targets[("license", row.Id)] = row.KeyHint;
            if (includeTracked)
                foreach (var row in db.Licenses.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true)))
                    result.Targets[("license", row.Id)] = row.KeyHint;
        }

        ids = Ids("binding");
        if (ids.Length > 0)
            foreach (var row in await (
                from binding in db.Bindings.AsNoTracking()join license in db.Licenses.AsNoTracking()on binding.LicenseId equals license.Id
                where ids.Contains(binding.Id)select new
                {
                    binding.Id,
                    license.KeyHint
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("binding", row.Id)] = row.KeyHint;
        ids = Ids("session");
        if (ids.Length > 0)
            foreach (var row in await (
                from session in db.Sessions.AsNoTracking()join binding in db.Bindings.AsNoTracking()on session.BindingId equals binding.Id
                join license in db.Licenses.AsNoTracking()on binding.LicenseId equals license.Id
                where ids.Contains(session.Id)select new
                {
                    session.Id,
                    license.KeyHint
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("session", row.Id)] = row.KeyHint;
        ids = Ids("offline-license");
        if (ids.Length > 0)
        {
            foreach (var row in await (
                from record in db.OfflineLicenses.AsNoTracking()join license in db.Licenses.AsNoTracking()on record.LicenseId equals license.Id
                where ids.Contains(record.Id)select new
                {
                    record.Id,
                    license.KeyHint,
                    record.Serial
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("offline-license", row.Id)] = $"{row.KeyHint} · {row.Serial.ToString("N")[..8]}";
            if (includeTracked)
            {
                var pending = db.OfflineLicenses.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true) && (access?.IncludesChannel(x.ChannelId) ?? true)).ToArray();
                var licenseIds = pending.Select(x => x.LicenseId).Distinct().ToArray();
                var hints = await db.Licenses.AsNoTracking().Where(x => licenseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.KeyHint, cancellationToken);
                foreach (var row in pending)
                    if (hints.TryGetValue(row.LicenseId, out var hint))
                        result.Targets[("offline-license", row.Id)] = $"{hint} · {row.Serial.ToString("N")[..8]}";
            }
        }

        ids = Ids("risk");
        if (ids.Length > 0)
            foreach (var row in await (
                from risk in db.RiskEvents.AsNoTracking()join license in db.Licenses.AsNoTracking()on risk.LicenseId equals license.Id
                where ids.Contains(risk.Id)select new
                {
                    risk.Id,
                    risk.Rule,
                    license.KeyHint
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("risk", row.Id)] = $"{row.KeyHint} · {row.Rule}";
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
            foreach (var row in await (
                from request in db.PortalRequests.AsNoTracking()join customer in db.Customers.AsNoTracking()on request.CustomerId equals customer.Id
                where ids.Contains(request.Id)select new
                {
                    request.Id,
                    request.Type,
                    customer.Name,
                    customer.AnonymizedAt
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("portal-request", row.Id)] = row.AnonymizedAt is null && !string.IsNullOrWhiteSpace(row.Name) ? $"{row.Name} · {(row.Type == "rebinding" ? "换绑申请" : "续期申请")}" : AnonymousReference("portal-request", row.Id);
        ids = Ids("channel");
        if (ids.Length > 0)
            foreach (var row in await db.Channels.AsNoTracking().Where(x => ids.Contains(x.Id) && (access == null || access.AllApplications || x.ApplicationIds.All(id => access.ApplicationIds.Contains(id)))).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                result.Targets[("channel", row.Id)] = row.Name;
        ids = Ids("api-credential");
        if (ids.Length > 0)
            foreach (var row in await db.ApiCredentials.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                result.Targets[("api-credential", row.Id)] = row.Name;
        ids = Ids("webhook");
        if (ids.Length > 0)
        {
            foreach (var row in await db.WebhookSubscriptions.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                result.Targets[("webhook", row.Id)] = row.Name;
            foreach (var row in await (
                from delivery in db.WebhookDeliveries.AsNoTracking()join subscription in db.WebhookSubscriptions.AsNoTracking()on delivery.SubscriptionId equals subscription.Id
                where ids.Contains(delivery.Id)select new
                {
                    delivery.Id,
                    subscription.Name
                }

            ).ToArrayAsync(cancellationToken))
                result.Targets[("webhook", row.Id)] = row.Name;
            if (includeTracked)
                foreach (var row in db.WebhookSubscriptions.Local.Where(x => ids.Contains(x.Id) && (access?.Includes(x.ApplicationId) ?? true)))
                    result.Targets[("webhook", row.Id)] = row.Name;
        }

        if (result.GlobalTargets)
        {
            var accountIds = Ids("account").Select(x => x.ToString()).ToArray();
            if (accountIds.Length > 0)
                foreach (var row in await db.Users.AsNoTracking().Where(x => accountIds.Contains(x.Id)).Select(x => new { x.Id, x.UserName }).ToArrayAsync(cancellationToken))
                    if (Guid.TryParse(row.Id, out var id) && row.UserName is not null)
                        result.Targets[("account", id)] = row.UserName;
            var roleIds = Ids("role").Select(x => x.ToString()).ToArray();
            if (roleIds.Length > 0)
                foreach (var row in await db.Roles.AsNoTracking().Where(x => roleIds.Contains(x.Id)).Select(x => new { x.Id, x.Name }).ToArrayAsync(cancellationToken))
                    if (Guid.TryParse(row.Id, out var id) && row.Name is not null)
                        result.Targets[("role", id)] = row.Name;
            ids = Ids("backup");
            if (ids.Length > 0)
                foreach (var row in await db.BackupJobs.AsNoTracking().Where(x => ids.Contains(x.Id)).Select(x => new { x.Id, x.CreatedAt }).ToArrayAsync(cancellationToken))
                    result.Targets[("backup", row.Id)] = row.CreatedAt.ToString("u", System.Globalization.CultureInfo.InvariantCulture);
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
        return new(row.Id, row.ActorId, row.Action, row.TargetId, row.Changes, row.CreatedAt, row.TraceId, row.Category, row.Result, row.ActorName ?? Actors.GetValueOrDefault(row.ActorId), type, targetName, row.ApplicationId, applicationName, row.SourceIp, row.Method, row.ErrorCategory);
    }

    internal static string AnonymousReference(string type, Guid id) => (type == "customer" ? "客户 " : "自助申请 ") + id.ToString("N")[..8];
}
