using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Infrastructure;

public sealed class DurableEventPublisher(AppDbContext db, TimeProvider clock, ApplicationAccessScope access) : IDurableEventPublisher
{
    public async Task<Guid> PublishAsync(Guid applicationId, Guid resourceId, string eventType, string safePayloadJson,
        string deduplicationKey, CancellationToken cancellationToken = default, Guid? channelId = null)
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("An existing transaction is required to publish a durable event.");
        if (!access.Includes(applicationId)) throw new RuleException("NOT_FOUND", 404);
        if (!access.IncludesChannel(channelId)) throw new RuleException("NOT_FOUND", 404);
        if (!OperationsEventTypes.All.Contains(eventType) || string.IsNullOrWhiteSpace(deduplicationKey) || deduplicationKey.Length > 1000 ||
            string.IsNullOrEmpty(safePayloadJson) || Encoding.UTF8.GetByteCount(safePayloadJson) > 16384) throw new RuleException("VALIDATION_ERROR", 400);
        using var payload = JsonDocument.Parse(safePayloadJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (payload.RootElement.ValueKind != JsonValueKind.Object || ContainsPrivateFields(payload.RootElement)) throw new RuleException("UNSAFE_EVENT_PAYLOAD", 400);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(applicationId + ":" + deduplicationKey));
        var key = Convert.ToHexString(digest);
        var advisory = BinaryPrimitives.ReadInt64BigEndian(digest);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({advisory})", cancellationToken);
        var previous = db.OperationsEvents.Local.FirstOrDefault(x => x.ApplicationId == applicationId && x.DeduplicationKey == key)
            ?? await db.OperationsEvents.SingleOrDefaultAsync(x => x.ApplicationId == applicationId && x.DeduplicationKey == key, cancellationToken);
        if (previous is not null) return previous.Id;
        var record = new OperationsEvent { ApplicationId = applicationId, ChannelId = channelId, ResourceId = resourceId, EventType = eventType, DeduplicationKey = key, CreatedAt = clock.GetUtcNow() };
        record.BodyJson = JsonSerializer.Serialize(new { id = record.Id, type = eventType, occurredAt = record.CreatedAt, applicationId, channelId, resourceId, data = payload.RootElement });
        db.OperationsEvents.Add(record);
        return record.Id;
    }

    private static bool ContainsPrivateFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().Any(ContainsPrivateFields);
        if (value.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in value.EnumerateObject())
        {
            if (property.Name.ToLowerInvariant() is "key" or "licensekey" or "secret" or "password" or "token" or "accesscode" or "email" or "phone" or "address" or "deviceid" or "ip" || ContainsPrivateFields(property.Value)) return true;
        }
        return false;
    }
}

internal static class ReliabilityRules
{
    public static void Page(int page, int pageSize)
    {
        if (page is < 1 or > 1000000 || pageSize is < 1 or > 100) throw new RuleException("VALIDATION_ERROR", 400);
    }
    public static string[] EventTypes(string[]? values)
    {
        if (values is null || values.Length > OperationsEventTypes.All.Length || values.Any(x => !OperationsEventTypes.All.Contains(x))) throw new RuleException("VALIDATION_ERROR", 400);
        return values.Distinct().Order(StringComparer.Ordinal).ToArray();
    }
    public static void Audit(AppDbContext db, Actor actor, string action, Guid target, Guid? applicationId, DateTimeOffset now, object? changes = null)
        => db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = target, ApplicationId = applicationId, CreatedAt = now, TraceId = actor.TraceId,
            Changes = JsonSerializer.Serialize(changes ?? new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
}

public sealed class RecipientAccessService(AppDbContext db)
{
    public async Task<string[]> Eligible(Guid applicationId, string[] ids, CancellationToken cancellationToken = default, Guid? channelId = null, bool forEvent = false)
    {
        if (ids.Length == 0) return [];
        var staff = await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id) && x.DisabledAt == null).ToArrayAsync(cancellationToken);
        var grants = await (from membership in db.UserRoles.AsNoTracking() join grant in db.RolePermissions.AsNoTracking() on membership.RoleId equals grant.RoleId
                            where ids.Contains(membership.UserId) select new { membership.UserId, grant.Permission }).Distinct().ToArrayAsync(cancellationToken);
        var channels = await db.Channels.AsNoTracking().Where(x => x.ApplicationIds.Contains(applicationId)).Select(x => x.Id).ToArrayAsync(cancellationToken);
        return staff.Where(user =>
        {
            var scope = RbacAccess.Scope(user, grants.Where(x => x.UserId == user.Id).Select(x => x.Permission));
            return scope.Permissions.Length > 0 && (scope.AllApplications || scope.ApplicationIds.Contains(applicationId)) &&
                (scope.ChannelId == null || channels.Contains(scope.ChannelId.Value) && (!forEvent || scope.ChannelId == channelId));
        }).Select(x => x.Id).ToArray();
    }
}
