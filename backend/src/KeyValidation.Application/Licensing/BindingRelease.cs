using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;
// Caller holds the application, license, binding and session locks through commit.
internal static class BindingRelease
{
    internal static async Task<bool> Execute(ILicensingData db, License license, DeviceBinding binding, DateTimeOffset now, IDurableEventPublisher? events)
    {
        if (binding.ReleasedAt != null)
            return false;
        var changes = db.RebindingEvents.AsNoTracking().Where(x => x.LicenseId == license.Id && x.Generation == license.RebindingGeneration && x.ChangedAt <= now);
        var start = now.AddDays(-license.RebindingWindowDays);
        license.RebindingStatus(now, await changes.Where(x => x.ChangedAt > start).Select(x => x.ChangedAt).ToArrayAsync(), await changes.MaxAsync(x => (DateTimeOffset? )x.ChangedAt)).EnsureAllowed();
        binding.ReleasedAt = now;
        foreach (var session in await db.Sessions.Where(x => x.BindingId == binding.Id && x.RevokedAt == null).OrderBy(x => x.Id).ToArrayAsync())
            session.Revoke(now);
        var fact = new DeviceRebindingEvent
        {
            LicenseId = license.Id,
            BindingId = binding.Id,
            Generation = license.RebindingGeneration,
            ChangedAt = now
        };
        db.RebindingEvents.Add(fact);
        if (events != null)
            await events.PublishAsync(license.ApplicationId, binding.Id, OperationsEventTypes.BindingReleased, JsonSerializer.Serialize(new { licenseId = license.Id, bindingId = binding.Id }), "binding-released:" + binding.Id, channelId: license.ChannelId);
        return true;
    }
}
