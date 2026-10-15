using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace KeyValidation.Infrastructure;
public sealed partial class AppDbContext
{
    private static void ConfigureAuditDisplay(ModelBuilder b)
    {
        b.Entity<AdminAuditLog>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(16).HasDefaultValue("operation");
            e.Property(x => x.Result).HasMaxLength(16).HasDefaultValue("succeeded");
            e.Property(x => x.ActorName).HasMaxLength(256);
            e.Property(x => x.TargetType).HasMaxLength(32);
            e.Property(x => x.TargetName).HasMaxLength(256);
            e.Property(x => x.ApplicationName).HasMaxLength(120);
            e.Property(x => x.SourceIp).HasMaxLength(64);
            e.Property(x => x.Method).HasMaxLength(32);
            e.Property(x => x.ErrorCategory).HasMaxLength(40);
            e.HasIndex(x => new { x.Category, x.CreatedAt });
        });
    }

    private async Task PopulateAuditDisplay(CancellationToken cancellationToken)
    {
        var rows = ChangeTracker.Entries<AdminAuditLog>().Where(x => x.State == EntityState.Added).Select(x => x.Entity).ToArray();
        if (rows.Length == 0)
            return;
        var labels = await AuditLabels.Load(this, rows, access, includeTracked: true, cancellationToken);
        foreach (var row in rows)
        {
            if (string.IsNullOrEmpty(row.Category))
                row.Category = AuditActions.Category(row.Action);
            if (string.IsNullOrEmpty(row.Result))
                row.Result = AuditActions.Result(row.Action, row.Changes);
            row.TargetType ??= AuditActions.TargetType(row.Action);
            row.ActorName ??= labels.Actors.GetValueOrDefault(row.ActorId);
            if (row.TargetType is "customer" or "portal-request")
                row.TargetName = AuditLabels.AnonymousReference(row.TargetType, row.TargetId);
            else if (row.TargetType is { } type)
                row.TargetName ??= labels.Targets.GetValueOrDefault((type, row.TargetId));
            if (row.ApplicationId is { } id)
                row.ApplicationName ??= labels.Applications.GetValueOrDefault(id);
        }
    }
}
