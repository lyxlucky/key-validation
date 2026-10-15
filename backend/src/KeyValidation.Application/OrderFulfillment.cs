using System.Text.Json;
using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

// The outer order or portal-review transaction owns the application/channel/
// customer/order locks. This service contributes facts and never commits alone.
public sealed class OrderFulfillment(ICommerceData db, ApplicationAccessScope access, CommerceWorkflow workflow, ISecretService secrets, IOrderDeliveryProtection protection, IDurableEventPublisher events)
{
    public async Task Execute(CommerceOrder order, Channel? channel, Actor actor)
    {
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Order fulfillment requires an outer transaction.");
        access.RequireWrite(ApplicationPermissions.ManageOrders);
        if (order.Status != "pending") throw new RuleException("ORDER_NOT_PENDING", 409);
        var debit = order.Type == "issue" ? order.Quantity : 1;
        if (channel != null && channel.RemainingQuota < debit) throw new RuleException("CHANNEL_QUOTA_EXHAUSTED", 409);
        var now = workflow.Now;
        if (order.Type == "issue")
        {
            var batch = new LicenseBatch { ApplicationId = order.ApplicationId, ChannelId = order.ChannelId, CustomerId = order.CustomerId, PlanId = order.PlanId, Quantity = order.Quantity,
                CreatedBy = actor.Id, CreatedAt = now, RequestId = order.Id, RequestFingerprint = order.RequestFingerprint };
            db.Batches.Add(batch); access.AuthorizeOrderBatch(batch.Id); order.BatchId = batch.Id;
            var keys = new List<IssuedKey>();
            for (var i = 0; i < order.Quantity; i++)
            {
                var key = secrets.GenerateLicense(); var license = License.Issue(order.ApplicationId, batch.Id, key.Digest, key.Version, key.Hint, order.DurationSeconds, order.MaxDevices,
                    order.MaxConcurrentSessions, order.ActivationValidityDays, order.Features, now);
                license.AssignCustomer(order.CustomerId, order.ChannelId); db.Licenses.Add(license); access.AuthorizeOrderLicense(license.Id);
                db.OrderLicenses.Add(new() { OrderId = order.Id, LicenseId = license.Id }); keys.Add(new(license.Id, key.Plaintext, key.Hint));
            }
            order.DeliveryCiphertext = protection.Protect(order.Id, keys); order.DeliveryExpiresAt = now.AddDays(30);
        }
        else
        {
            await workflow.LockLicense(order.RenewalLicenseId!.Value);
            var license = await db.Licenses.SingleOrDefaultAsync(x => x.Id == order.RenewalLicenseId && x.CustomerId == order.CustomerId && x.ApplicationId == order.ApplicationId && x.ChannelId == order.ChannelId && !x.IsTrial)
                ?? throw new RuleException("NOT_FOUND", 404);
            now = workflow.Now; license.Extend(now, order.DurationSeconds); access.AuthorizeOrderLicense(license.Id);
            db.OrderLicenses.Add(new() { OrderId = order.Id, LicenseId = license.Id });
            await events.PublishAsync(order.ApplicationId, license.Id, OperationsEventTypes.LicenseRenewed,
                JsonSerializer.Serialize(new { orderId = order.Id, licenseId = license.Id, additionalSeconds = order.DurationSeconds }), "order-renewal:" + order.Id, channelId: order.ChannelId);
        }
        if (channel != null)
        {
            channel.RemainingQuota -= debit; channel.UpdatedAt = now; access.AuthorizeOrderChannel(channel.Id);
            db.ChannelQuotaEntries.Add(new() { ChannelId = channel.Id, Kind = "fulfillment", Delta = -debit, BalanceAfter = channel.RemainingQuota, Reason = "Order fulfillment", ActorId = actor.Id,
                CreatedAt = now, OrderId = order.Id, RequestId = order.Id, Fingerprint = order.RequestFingerprint });
        }
        order.Status = "fulfilled"; order.FulfilledBy = actor.Id; order.FulfilledAt = now;
        db.OrderHistories.Add(new() { OrderId = order.Id, Action = "fulfilled", ActorId = actor.Id, CreatedAt = now });
        workflow.Audit(actor, "order.fulfill", order.Id, order.ApplicationId, order.ChannelId, new { order.Type, order.Quantity, order.CustomerId, order.BatchId }, "订单 " + order.Id.ToString("N")[..8]);
        await events.PublishAsync(order.ApplicationId, order.Id, OperationsEventTypes.OrderFulfilled,
            JsonSerializer.Serialize(new { orderId = order.Id, order.Type, order.Quantity, order.BatchId }), "order-fulfilled:" + order.Id, channelId: order.ChannelId);
    }
}
