using KeyValidation.Domain;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Application;

public sealed class OrderService(ICommerceData db, ApplicationAccessScope access, CommerceWorkflow workflow, OrderFulfillment fulfillment, IOrderDeliveryProtection protection, PlanAvailability planAvailability)
{
    public async Task<OrderCatalogDto> Catalog(Guid? customerId)
    {
        access.Require(ApplicationPermissions.ManageOrders);
        var customers = await db.Customers.AsNoTracking().Where(x => x.DisabledAt == null && x.AnonymizedAt == null).OrderBy(x => x.Name).ThenBy(x => x.Id)
            .Select(x => new OrderCustomerOption(x.Id, x.ApplicationId, x.ChannelId, x.Name)).ToArrayAsync();
        if (customerId != null && !await db.Customers.AnyAsync(x => x.Id == customerId)) throw new RuleException("NOT_FOUND", 404);
        var licenses = customerId == null ? [] : await db.Licenses.AsNoTracking().Where(x => x.CustomerId == customerId && !x.IsTrial && x.FrozenAt == null)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new OrderLicenseOption(x.Id, x.CustomerId!.Value, x.KeyHint)).ToArrayAsync();
        return new(customers, licenses);
    }
    public async Task<PagedResult<OrderDto>> List(int page, int pageSize, Guid? applicationId, Guid? channelId, Guid? customerId, string? status, Guid? requestId = null)
    {
        access.Require(ApplicationPermissions.ManageOrders); CommerceWorkflow.Page(page, pageSize);
        CommerceWorkflow.Require(status is null or "" or "pending" or "fulfilled" or "cancelled");
        var query = db.Orders.AsNoTracking().Where(x => (applicationId == null || x.ApplicationId == applicationId) && (channelId == null || x.ChannelId == channelId) &&
            (customerId == null || x.CustomerId == customerId) && (status == null || status == "" || x.Status == status) && (requestId == null || x.RequestId == requestId));
        var count = await query.CountAsync(); var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize).ToArrayAsync();
        return new(await Dtos(rows), count, page, pageSize);
    }
    public async Task<OrderDto> Get(Guid id)
    {
        access.Require(ApplicationPermissions.ManageOrders);
        var order = await Find(id); return (await Dtos([order]))[0];
    }
    public async Task<OrderDto> Create(CreateOrder request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); Validate(request);
        var fingerprint = CommerceWorkflow.Fingerprint(request);
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.RequestLock(actor.Id, "order.create", request.RequestId);
        if (await workflow.Receipt(actor, "order.create", request.RequestId, fingerprint, ApplicationPermissions.ManageOrders) is { } previous) return await Get(previous);
        var location = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.CustomerId && x.ApplicationId == request.ApplicationId) ?? throw new RuleException("NOT_FOUND", 404);
        await workflow.LockApplication(request.ApplicationId); await workflow.LockChannel(location.ChannelId, request.ApplicationId); var customer = await workflow.LockCustomer(location.Id);
        var order = await CreateUnderLock(request, customer, actor);
        workflow.Record(actor, "order.create", request.RequestId, fingerprint, order.Id);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await Get(order.Id);
    }
    internal async Task<CommerceOrder> CreateUnderLock(CreateOrder request, Customer customer, Actor actor)
    {
        Validate(request);
        var plan = await planAvailability.Require(request.ApplicationId, request.PlanId);
        var price = request.UnitPriceCents;
        if (customer.ChannelId is { } channelId)
            price = await db.ChannelPrices.Where(x =>
                x.ChannelId == channelId &&
                x.ApplicationId == request.ApplicationId &&
                x.PlanId == plan.Id)
                .Select(x => (long?)x.UnitPriceCents).SingleOrDefaultAsync()
                ?? throw new RuleException("CHANNEL_PRICE_NOT_CONFIGURED", 409);
        var duration = request.Type == "renewal" ? request.AdditionalSeconds ?? plan.DurationSeconds : plan.DurationSeconds;
        if (request.Type == "renewal")
        {
            CommerceWorkflow.Require(duration is >= 3600 and <= 315360000 && duration % 3600 == 0);
            await workflow.LockLicense(request.LicenseId!.Value);
            var license = await db.Licenses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.LicenseId && x.CustomerId == customer.Id && x.ApplicationId == customer.ApplicationId && x.ChannelId == customer.ChannelId && !x.IsTrial)
                ?? throw new RuleException("NOT_FOUND", 404);
            if (license.FrozenAt != null) throw new RuleException("LICENSE_FROZEN");
        }
        var order = new CommerceOrder
        {
            ApplicationId = customer.ApplicationId,
            ChannelId = customer.ChannelId,
            CustomerId = customer.Id,
            PlanId = plan.Id,
            Type = request.Type,
            Quantity = request.Quantity,
            UnitPriceCents = price,
            RequestId = request.RequestId,
            RequestFingerprint = CommerceWorkflow.Fingerprint(request),
            RenewalLicenseId = request.LicenseId,
            PlanName = plan.Name,
            DurationSeconds = duration,
            MaxDevices = plan.MaxDevices,
            MaxConcurrentSessions = plan.MaxConcurrentSessions,
            ActivationValidityDays = plan.ActivationValidityDays,
            Features = plan.Features.ToArray(),
            CreatedBy = actor.Id,
            CreatedAt = workflow.Now
        };
        db.Orders.Add(order); History(order, actor, "created");
        workflow.Audit(actor, "order.create", order.Id, order.ApplicationId, order.ChannelId, new { order.Type, order.Quantity, order.UnitPriceCents, order.CustomerId, order.PlanId }, Label(order));
        return order;
    }
    public async Task<OrderDto> Fulfill(Guid id, FulfillOrder request, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); CommerceWorkflow.Require(request.RequestId != Guid.Empty);
        var fingerprint = CommerceWorkflow.Fingerprint(new { id });
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.RequestLock(actor.Id, "order.fulfill", request.RequestId);
        if (await workflow.Receipt(actor, "order.fulfill", request.RequestId, fingerprint, ApplicationPermissions.ManageOrders) is { } previous) return await Get(previous);
        var location = await Find(id);
        var app = await workflow.LockApplication(location.ApplicationId, false); var channel = await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        var customer = await workflow.LockCustomer(location.CustomerId, false);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE");
        var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
        if (order.Status == "cancelled") throw new RuleException("ORDER_CANCELLED", 409);
        if (order.Status == "pending")
        {
            if (app.DisabledAt != null) throw new RuleException("APPLICATION_DISABLED");
            if (channel is { IsEnabled: false }) throw new RuleException("CHANNEL_DISABLED");
            if (customer.DisabledAt != null || customer.AnonymizedAt != null) throw new RuleException("CUSTOMER_DISABLED");
            await fulfillment.Execute(order, channel, actor);
        }
        workflow.Record(actor, "order.fulfill", request.RequestId, fingerprint, id);
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await Get(id);
    }
    public async Task<OrderDto> Cancel(Guid id, Actor actor)
    {
        access.RequireWrite(ApplicationPermissions.ManageOrders); var location = await Find(id);
        await using var tx = await db.Database.BeginTransactionAsync(); await workflow.LockApplication(location.ApplicationId, false); await workflow.LockChannel(location.ChannelId, location.ApplicationId, false);
        await workflow.LockCustomer(location.CustomerId, false);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Orders\" WHERE \"Id\" = {id} FOR UPDATE");
        var order = await db.Orders.SingleAsync(x => x.Id == id);
        if (order.Status != "pending") throw new RuleException("ORDER_NOT_PENDING", 409);
        order.Status = "cancelled"; order.CancelledAt = workflow.Now; History(order, actor, "cancelled");
        workflow.Audit(actor, "order.cancel", id, order.ApplicationId, order.ChannelId, label: Label(order));
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await Get(id);
    }
    public async Task<OrderDeliveryDto> Delivery(Guid id)
    {
        access.Require(ApplicationPermissions.ManageOrders);
        await using var tx = await db.Database.BeginTransactionAsync();
        await access.RecheckWrite(ApplicationPermissions.ManageOrders);
        var order = await Find(id);
        if (order.DeliveryCiphertext == null || order.DeliveryExpiresAt <= workflow.Now) throw new RuleException("DELIVERY_UNAVAILABLE", 410);
        var keys = protection.Unprotect(id, order.DeliveryCiphertext);
        await tx.CommitAsync(); return new(id, order.DeliveryExpiresAt!.Value, keys);
    }
    private static void Validate(CreateOrder request)
    {
        CommerceWorkflow.Require(request.RequestId != Guid.Empty && request.ApplicationId != Guid.Empty && request.CustomerId != Guid.Empty && request.PlanId != Guid.Empty &&
            request.Type is "issue" or "renewal" && request.Quantity is >= 1 and <= 100 && request.UnitPriceCents is >= 0 and <= CommerceWorkflow.MaxUnitPriceCents);
        CommerceWorkflow.Require(request.Type == "renewal" ? request.Quantity == 1 && request.LicenseId is { } id && id != Guid.Empty : request.LicenseId == null && request.AdditionalSeconds == null);
    }
    private async Task<CommerceOrder> Find(Guid id) => await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id) ?? throw new RuleException("NOT_FOUND", 404);
    private void History(CommerceOrder order, Actor actor, string action) => db.OrderHistories.Add(new() { OrderId = order.Id, ActorId = actor.Id, Action = action, CreatedAt = workflow.Now });
    private async Task<OrderDto[]> Dtos(CommerceOrder[] rows)
    {
        var ids = rows.Select(x => x.Id).ToArray(); var links = await db.OrderLicenses.AsNoTracking().Where(x => ids.Contains(x.OrderId)).OrderBy(x => x.LicenseId).ToArrayAsync();
        var history = await db.OrderHistories.AsNoTracking().Where(x => ids.Contains(x.OrderId)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArrayAsync(); var now = workflow.Now;
        return rows.Select(x => new OrderDto(x.Id, x.ApplicationId, x.ChannelId, x.CustomerId, x.PlanId, x.Type, x.Status, x.Quantity, x.UnitPriceCents, checked(x.Quantity * x.UnitPriceCents), "CNY", x.RequestId, x.RenewalLicenseId, x.BatchId,
            new(x.PlanName, x.DurationSeconds, x.MaxDevices, x.MaxConcurrentSessions, x.ActivationValidityDays, x.Features), x.CreatedBy, x.CreatedAt, x.FulfilledBy, x.FulfilledAt, x.CancelledAt,
            x.DeliveryCiphertext != null && x.DeliveryExpiresAt > now, x.DeliveryExpiresAt, links.Where(l => l.OrderId == x.Id).Select(l => l.LicenseId).ToArray(),
            history.Where(h => h.OrderId == x.Id).Select(h => new OrderHistoryDto(h.Action, h.ActorId, h.CreatedAt)).ToArray())).ToArray();
    }
    private static string Label(CommerceOrder order) => "订单 " + order.Id.ToString("N")[..8];
}
