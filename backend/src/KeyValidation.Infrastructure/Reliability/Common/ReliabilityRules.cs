using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;
internal static class ReliabilityRules
{
    public static void Page(int page, int pageSize)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100)
            throw new RuleException("VALIDATION_ERROR", 400);
    }

    public static string[] EventTypes(string[]? values)
    {
        if (values is null || values.Length > OperationsEventTypes.All.Length || values.Any(x => !OperationsEventTypes.All.Contains(x)))
            throw new RuleException("VALIDATION_ERROR", 400);
        return values.Distinct().Order(StringComparer.Ordinal).ToArray();
    }

    public static void Audit(AppDbContext db, Actor actor, string action, Guid target, Guid? applicationId, DateTimeOffset now, object? changes = null) => db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = target, ApplicationId = applicationId, CreatedAt = now, TraceId = actor.TraceId, Changes = JsonSerializer.Serialize(changes ?? new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
}
