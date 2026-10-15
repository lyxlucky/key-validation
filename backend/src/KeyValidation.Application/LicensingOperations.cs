using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed partial class LicensingService
{
    private static readonly JsonSerializerOptions ReceiptJson = new(JsonSerializerDefaults.Web);

    private static LicenseOperationRequest NormalizeOperation(LicenseOperationRequest request)
    {
        Require(request.RequestId != Guid.Empty && request.LicenseIds is { Length: >= 1 and <= 100 });
        var ids = request.LicenseIds!;
        Require(ids.All(id => id != Guid.Empty) && ids.Distinct().Count() == ids.Length);
        var action = request.Action?.Trim().ToLowerInvariant();
        Require(action is "disable" or "enable" or "extend" or "metadata");
        var reason = request.Reason?.Trim();
        Require(!string.IsNullOrEmpty(reason) && reason.Length <= 500);
        var note = request.Note?.Trim();
        Require((note?.Length ?? 0) <= 500 && (request.Tags?.Length ?? 0) <= 10);
        var tags = new List<string>();
        foreach (var tag in request.Tags ?? [])
        {
            Require(tag is not null);
            var normalized = tag!.Trim();
            Require(normalized.Length is >= 1 and <= 32);
            tags.Add(normalized);
        }
        if (action == "extend")
            Require(request.AdditionalSeconds is >= 3600 and <= 315360000 && request.AdditionalSeconds % 3600 == 0);
        else Require(request.AdditionalSeconds is null);
        if (action != "metadata") Require(request.Note is null && request.Tags is null);
        return request with
        {
            LicenseIds = ids.Order().ToArray(), Action = action!, Reason = reason!,
            Note = string.IsNullOrEmpty(note) ? null : note,
            Tags = action == "metadata" ? tags.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray() : null
        };
    }

    public async Task<OperationResult> LicenseOperation(LicenseOperationRequest request, Actor actor)
    {
        DemandWrite(ApplicationPermissions.ManageLicenses);
        request = NormalizeOperation(request);
        if (access?.ChannelId != null && request.Action == "extend") throw new RuleException("CHANNEL_ORDER_REQUIRED", 403);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, ReceiptJson)));
        var advisoryKey = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes("license-operation:" + actor.Id + ":" + request.RequestId)));
        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({advisoryKey})");
        var previous = await db.OperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.Id && x.RequestId == request.RequestId);
        if (previous is not null)
        {
            if (access is not null) await access.RecheckWrite(ApplicationPermissions.ManageLicenses);
            if (previous.Fingerprint != fingerprint) throw new RuleException("IDEMPOTENCY_CONFLICT", 409);
            return await VisibleReceipt(previous);
        }

        // Acquire every parent first, in PostgreSQL UUID order. The existing helper
        // then locks all licenses, bindings and sessions in the shared lock order.
        var applicationIds = await db.Licenses.AsNoTracking().Where(x => request.LicenseIds.Contains(x.Id))
            .Select(x => x.ApplicationId).Distinct().OrderBy(x => x).ToArrayAsync();
        foreach (var applicationId in applicationIds) await LockApp(applicationId);
        var visibleIds = await db.Licenses.Where(x => request.LicenseIds.Contains(x.Id)).Select(x => x.Id).ToArrayAsync();
        await LockLicenses(visibleIds);
        var licenses = await db.Licenses.Where(x => request.LicenseIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        var now = Now;
        var items = new List<LicenseOperationItem>(request.LicenseIds.Length);
        foreach (var id in request.LicenseIds)
        {
            if (!licenses.TryGetValue(id, out var license))
            {
                items.Add(new(id, false, "NOT_FOUND", null));
                Audit(actor, "license." + request.Action, id, new { request.RequestId, request.Reason, success = false, errorCode = "NOT_FOUND" });
                continue;
            }
            var before = LicenseDtoAt(license, now);
            try
            {
                switch (request.Action)
                {
                    case "disable": license.Disable(now); break;
                    case "enable": license.Enable(); break;
                    case "extend":
                        if (license.ChannelId != null) throw new RuleException("CHANNEL_ORDER_REQUIRED", 403);
                        license.Extend(now, request.AdditionalSeconds!.Value); break;
                    case "metadata": license.Note = request.Note; license.Tags = request.Tags!.ToArray(); break;
                }
            }
            catch (RuleException error) when (error.Code is "DURATION_LIMIT_EXCEEDED" or "LICENSE_FROZEN")
            {
                items.Add(new(id, false, error.Code, null));
                Audit(actor, "license." + request.Action, id, new { request.RequestId, request.Reason, success = false, errorCode = error.Code });
                continue;
            }
            var after = LicenseDtoAt(license, now);
            items.Add(new(id, true, null, after));
            Audit(actor, "license." + request.Action, id, new { request.RequestId, request.Reason, request.AdditionalSeconds, success = true, before, after });
        }
        if (request.Action == "disable") await RevokeForLicenses(licenses.Keys.ToArray());
        var result = new OperationResult(request.RequestId, request.Action, now, items.Count(x => x.Success), items.Count(x => !x.Success), items);
        db.OperationReceipts.Add(new() { ActorId = actor.Id, RequestId = request.RequestId, Fingerprint = fingerprint, ResultJson = JsonSerializer.Serialize(result, ReceiptJson), CreatedAt = now });
        if (events is not null)
            foreach (var group in licenses.Values.GroupBy(x => new { x.ApplicationId, x.ChannelId }))
            {
                var groupIds = group.Select(x => x.Id).ToHashSet();
                var visible = items.Where(item => groupIds.Contains(item.LicenseId)).ToArray();
                await events.PublishAsync(group.Key.ApplicationId, request.RequestId, OperationsEventTypes.BatchCompleted,
                    JsonSerializer.Serialize(new { request.RequestId, request.Action, succeeded = visible.Count(x => x.Success), failed = visible.Count(x => !x.Success) }),
                    "license-operation:" + actor.Id + ":" + request.RequestId + ":" + group.Key.ChannelId, channelId: group.Key.ChannelId);
            }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return result;
    }

    public async Task<OperationResult> LicenseOperationReceipt(Guid requestId, Actor actor)
    {
        Demand(ApplicationPermissions.ReadLicenses);
        var receipt = await db.OperationReceipts.AsNoTracking().SingleOrDefaultAsync(x => x.ActorId == actor.Id && x.RequestId == requestId)
            ?? throw new RuleException("NOT_FOUND", 404);
        return await VisibleReceipt(receipt);
    }

    private async Task<OperationResult> VisibleReceipt(LicenseOperationReceipt receipt)
    {
        var result = DeserializeReceipt(receipt);
        if (access is null || access.AllApplications && access.ChannelId == null) return result;
        var ids = result.Items.Select(x => x.LicenseId).ToArray();
        var visible = (await db.Licenses.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToArrayAsync()).ToHashSet();
        var items = result.Items.Select(x => visible.Contains(x.LicenseId) ? x : new LicenseOperationItem(x.LicenseId, false, "NOT_FOUND", null)).ToArray();
        return result with { Items = items, Succeeded = items.Count(x => x.Success), Failed = items.Count(x => !x.Success) };
    }

    private static OperationResult DeserializeReceipt(LicenseOperationReceipt receipt) =>
        JsonSerializer.Deserialize<OperationResult>(receipt.ResultJson, ReceiptJson) ?? throw new InvalidOperationException("Stored license operation result is invalid.");
}
