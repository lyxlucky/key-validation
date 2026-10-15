using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class PortalRequestService(ICommerceData db, ApplicationAccessScope access, CommerceWorkflow workflow, OrderService orders, OrderFulfillment fulfillment, IDurableEventPublisher events)
{
    public async Task<PagedResult<PortalRequestDto>> List(int page, int pageSize, Guid? applicationId, Guid? channelId, Guid? customerId, string? status)
    {
        access.Require(ApplicationPermissions.ManageOrders); CommerceWorkflow.Page(page, pageSize); CommerceWorkflow.Require(status is null or "" or "pending" or "approved" or "rejected");
        var query = db.PortalRequests.AsNoTracking().Where(x => (applicationId == null || x.ApplicationId == applicationId) && (channelId == null || x.ChannelId == channelId) &&
            (customerId == null || x.CustomerId == customerId) && (status == null || status == "" || x.Status == status));
        var total = await query.CountAsync(); var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(rows.Select(Dto).ToArray(), total, page, pageSize);
    }
    public async Task<PortalRequestDto> Create(CreatePortalRequest request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); var customerId = access.CustomerId ?? throw new RuleException("UNAUTHENTICATED", 401);
        CommerceWorkflow.Require(request.RequestId != Guid.Empty && request.LicenseId != Guid.Empty && request.Type is "renewal" or "rebinding" && (request.Type == "rebinding" ? request.BindingId != null && request.BindingId != Guid.Empty : request.BindingId == null));
        var note = CommerceWorkflow.Optional(request.Note, 500); var fingerprint = CommerceWorkflow.Fingerprint(request with { Note = note });
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.RequestLock(actor.Id, "portal.request", request.RequestId);
        var previous = await db.PortalRequests.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.RequestId == request.RequestId);
        if (previous != null)
        {
            await access.RecheckWrite(ApplicationPermissions.ManageOrders);
            if (previous.Fingerprint != fingerprint) throw new RuleException("IDEMPOTENCY_CONFLICT", 409);
            return Dto(previous);
        }
        var location = await db.Customers.AsNoTracking().SingleAsync(x => x.Id == customerId);
        await workflow.LockApplication(location.ApplicationId); await workflow.LockChannel(location.ChannelId, location.ApplicationId); await workflow.LockCustomer(customerId);
        await workflow.LockLicense(request.LicenseId);
        var license = await db.Licenses.SingleOrDefaultAsync(x => x.Id == request.LicenseId && x.CustomerId == customerId && !x.IsTrial) ?? throw new RuleException("NOT_FOUND", 404);
        if (request.Type == "renewal" && license.FrozenAt != null) throw new RuleException("LICENSE_FROZEN");
        if (request.Type == "rebinding" && !await db.Bindings.AnyAsync(x => x.Id == request.BindingId && x.LicenseId == license.Id && x.ReleasedAt == null)) throw new RuleException("NOT_FOUND", 404);
        var item = new PortalRequest { ApplicationId = location.ApplicationId, ChannelId = location.ChannelId, CustomerId = customerId, Type = request.Type, LicenseId = request.LicenseId,
            BindingId = request.BindingId, Note = note, RequestId = request.RequestId, Fingerprint = fingerprint, CreatedAt = workflow.Now };
        db.PortalRequests.Add(item); workflow.Audit(actor, "portal-request.create", item.Id, item.ApplicationId, item.ChannelId, new { item.Type, item.LicenseId, item.BindingId }, Label(item));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Dto(item);
    }
    public async Task<PortalRequestDto> Review(Guid id, ReviewPortalRequest request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); var reason = CommerceWorkflow.Text(request.Reason, 500); CommerceWorkflow.Require(request.Status is "approved" or "rejected");
        var fingerprint = CommerceWorkflow.Fingerprint(request with { Reason = reason });
        var location = await db.PortalRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); var app = await workflow.LockApplication(location.ApplicationId, false); var channel = await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        var customer = await workflow.LockCustomer(location.CustomerId, false);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"PortalRequests\" WHERE \"Id\" = {id} FOR UPDATE");
        var item = await db.PortalRequests.SingleAsync(x => x.Id == id);
        if (item.Status != "pending")
        {
            await access.RecheckWrite(ApplicationPermissions.ManageOrders);
            if (item.ReviewFingerprint != fingerprint) throw new RuleException("IDEMPOTENCY_CONFLICT", 409);
            return Dto(item);
        }
        if (request.Status == "approved")
        {
            if (app.DisabledAt != null) throw new RuleException("APPLICATION_DISABLED");
            if (channel is { IsEnabled: false }) throw new RuleException("CHANNEL_DISABLED");
            if (customer.DisabledAt != null || customer.AnonymizedAt != null) throw new RuleException("CUSTOMER_DISABLED");
            if (item.Type == "renewal")
            {
                CommerceWorkflow.Require(request.PlanId != null && request.UnitPriceCents is >= 0);
                var order = await orders.CreateUnderLock(new(item.Id, item.ApplicationId, item.CustomerId, request.PlanId!.Value, "renewal", 1, request.UnitPriceCents!.Value, item.LicenseId, request.AdditionalSeconds), customer, actor);
                await fulfillment.Execute(order, channel, actor); item.OrderId = order.Id;
            }
            else
            {
                CommerceWorkflow.Require(request.PlanId == null && request.UnitPriceCents == null && request.AdditionalSeconds == null);
                await workflow.LockLicense(item.LicenseId);
                var license = await db.Licenses.SingleOrDefaultAsync(x => x.Id == item.LicenseId && x.CustomerId == item.CustomerId) ?? throw new RuleException("NOT_FOUND", 404);
                var binding = await db.Bindings.SingleOrDefaultAsync(x => x.Id == item.BindingId && x.LicenseId == license.Id) ?? throw new RuleException("NOT_FOUND", 404);
                access.AuthorizeOrderBinding(binding.Id);
                await BindingRelease.Execute(db, license, binding, workflow.Now, events);
            }
        }
        item.Status = request.Status; item.ReviewReason = reason; item.ReviewedAt = workflow.Now; item.ReviewedBy = actor.Id; item.ReviewFingerprint = fingerprint;
        workflow.Audit(actor, "portal-request.review", item.Id, item.ApplicationId, item.ChannelId, new { item.Type, item.Status, item.OrderId, item.LicenseId }, Label(item));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Dto(item);
    }
    private static PortalRequestDto Dto(PortalRequest x) => new(x.Id, x.ApplicationId, x.ChannelId, x.CustomerId, x.Type, x.LicenseId, x.BindingId, x.Note, x.Status, x.RequestId, x.CreatedAt, x.ReviewedAt, x.ReviewedBy, x.ReviewReason, x.OrderId);
    private static string Label(PortalRequest x) => "自助申请 " + x.Id.ToString("N")[..8];
}
