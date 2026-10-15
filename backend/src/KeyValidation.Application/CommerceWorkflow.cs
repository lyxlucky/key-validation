using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

// Shared transaction vocabulary. Callers own begin/commit; every fact writer
// holds the application row before reading Now, in the licensing lock order.
public sealed class CommerceWorkflow(ICommerceData db, ApplicationAccessScope access, TimeProvider clock)
{
    public const long MaxExactInteger = 9_007_199_254_740_991;
    public const long MaxUnitPriceCents = MaxExactInteger / 100;
    public DateTimeOffset Now { get { var now = clock.GetUtcNow(); return new(now.UtcTicks - now.UtcTicks % 10, TimeSpan.Zero); } }
    public static void Require(bool condition) { if (!condition) throw new RuleException("VALIDATION_ERROR", 400); }
    public static void Page(int page, int size) => Require(page is >= 1 and <= 1000000 && size is >= 1 and <= 100);
    public static string Text(string? value, int max = 120) { value = value?.Trim(); Require(value is { Length: > 0 } && value.Length <= max); return value!; }
    public static string? Optional(string? value, int max) { Require(value?.Length <= max || value is null); return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
    public static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string NewSecret(string prefix) => prefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public async Task<SoftwareApplication> LockApplication(Guid id, bool business = true)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Applications\" WHERE \"Id\" = {id} FOR SHARE");
        var app = await db.Applications.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        if (business && app.DisabledAt != null) throw new RuleException("APPLICATION_DISABLED");
        return app;
    }
    public async Task<Channel?> LockChannel(Guid? id, Guid app, bool business = true)
    {
        if (!access.IncludesChannel(id)) throw new RuleException("NOT_FOUND", 404);
        if (id is null) return null;
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Channels\" WHERE \"Id\" = {id} FOR UPDATE");
        var channel = await db.Channels.SingleOrDefaultAsync(x => x.Id == id && x.ApplicationIds.Contains(app)) ?? throw new RuleException("NOT_FOUND", 404);
        if (business && !channel.IsEnabled) throw new RuleException("CHANNEL_DISABLED");
        return channel;
    }
    public async Task<Customer> LockCustomer(Guid id, bool business = true)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Customers\" WHERE \"Id\" = {id} FOR UPDATE");
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        if (business && (customer.DisabledAt != null || customer.AnonymizedAt != null)) throw new RuleException("CUSTOMER_DISABLED");
        return customer;
    }
    public async Task LockLicense(Guid id)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Licenses\" WHERE \"Id\" = {id} FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Bindings\" WHERE \"LicenseId\" = {id} ORDER BY \"Id\" FOR UPDATE");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT s.\"Id\" FROM \"Sessions\" s JOIN \"Bindings\" b ON b.\"Id\" = s.\"BindingId\" WHERE b.\"LicenseId\" = {id} ORDER BY s.\"Id\" FOR UPDATE OF s");
    }
    public async Task RequestLock(string actor, string operation, Guid requestId)
    {
        Require(requestId != Guid.Empty);
        var key = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes($"commerce:{actor}:{operation}:{requestId}")));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({key})");
    }
    public async Task<Guid?> Receipt(Actor actor, string operation, Guid requestId, string fingerprint, string permission)
    {
        var previous = await db.CommerceReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.Id && x.Operation == operation && x.RequestId == requestId);
        if (previous is null) return null;
        await access.RecheckWrite(permission);
        if (previous.Fingerprint != fingerprint) throw new RuleException("IDEMPOTENCY_CONFLICT", 409);
        return previous.ResourceId;
    }
    public void Record(Actor actor, string operation, Guid requestId, string fingerprint, Guid id) =>
        db.CommerceReceipts.Add(new() { ActorId = actor.Id, Operation = operation, RequestId = requestId, Fingerprint = fingerprint, ResourceId = id, CreatedAt = Now });
    public void Audit(Actor actor, string action, Guid id, Guid? app, Guid? channel, object? facts = null, string? label = null) =>
        db.AuditLogs.Add(new() { ActorId = actor.Id, Action = action, TargetId = id, ApplicationId = app, ChannelId = channel,
            TargetName = label, CreatedAt = Now, TraceId = actor.TraceId, Changes = JsonSerializer.Serialize(facts ?? new { }, new JsonSerializerOptions(JsonSerializerDefaults.Web)) });
    public static CustomerDto CustomerDto(Customer x) => new(x.Id, x.ApplicationId, x.ChannelId, x.Name, x.Email, x.Phone, x.Note, x.CreatedAt, x.UpdatedAt, x.DisabledAt, x.AnonymizedAt, x.AccessCodeDigest != null);
    public static LicenseDto LicenseDto(License l, DateTimeOffset now) => new(l.Id, l.ApplicationId, l.BatchId, l.KeyHint, l.DurationSeconds, l.MaxDevices, l.Status(now), l.CreatedAt, l.ActivatedAt, l.ExpiresAt,
        l.DisabledAt, l.Note, l.Tags, l.MaxConcurrentSessions, l.ActivateBefore, l.FrozenAt, l.FrozenRemainingSeconds, l.Features, l.IsTrial);
}
