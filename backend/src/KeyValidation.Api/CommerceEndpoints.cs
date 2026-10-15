using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class CommerceEndpoints
{
    internal static Actor Actor(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    public static void MapCommerceApi(this WebApplication app)
    {
        var customers = app.MapGroup("/api/admin/customers").WithTags("Customers").RequireAuthorization("Read", ApplicationPermissions.ManageCustomers);
        customers.MapGet("/", (CustomerService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null, string? search = null) => service.List(page, pageSize, applicationId, channelId, search));
        customers.MapGet("/{id:guid}", (Guid id, CustomerService service) => service.Get(id));
        customers.MapPost("/", async (CreateCustomer request, HttpContext context, CustomerService service) => { var result = await service.Create(request, Actor(context)); return TypedResults.Created($"/api/admin/customers/{result.Id}", result); });
        customers.MapPatch("/{id:guid}", (Guid id, UpdateCustomer request, HttpContext context, CustomerService service) => service.Update(id, request, Actor(context)));
        customers.MapPost("/{id:guid}/disable", (Guid id, HttpContext context, CustomerService service) => service.SetAccess(id, "disable", Actor(context)));
        customers.MapPost("/{id:guid}/enable", (Guid id, HttpContext context, CustomerService service) => service.SetAccess(id, "enable", Actor(context)));
        customers.MapPost("/{id:guid}/anonymize", (Guid id, HttpContext context, CustomerService service) => service.SetAccess(id, "anonymize", Actor(context)));
        customers.MapPost("/{id:guid}/rotate-access-code", (Guid id, HttpContext context, CustomerService service) => service.RotateAccessCode(id, Actor(context)));
        customers.MapGet("/{id:guid}/licenses", (Guid id, CustomerService service, int page = 1, int pageSize = 20) => service.Licenses(id, page, pageSize));

        var orders = app.MapGroup("/api/admin/orders").WithTags("Orders").RequireAuthorization("Read", ApplicationPermissions.ManageOrders);
        orders.MapGet("/", (OrderService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null, Guid? customerId = null, string? status = null, Guid? requestId = null) => service.List(page, pageSize, applicationId, channelId, customerId, status, requestId));
        orders.MapGet("/catalog", (OrderService service, Guid? customerId = null) => service.Catalog(customerId));
        orders.MapGet("/{id:guid}", (Guid id, OrderService service) => service.Get(id));
        orders.MapPost("/", async (CreateOrder request, HttpContext context, OrderService service) => { var result = await service.Create(request, Actor(context)); return TypedResults.Created($"/api/admin/orders/{result.Id}", result); });
        orders.MapPost("/{id:guid}/fulfill", (Guid id, FulfillOrder request, HttpContext context, OrderService service) => service.Fulfill(id, request, Actor(context)));
        orders.MapPost("/{id:guid}/cancel", (Guid id, HttpContext context, OrderService service) => service.Cancel(id, Actor(context)));
        orders.MapGet("/{id:guid}/delivery", (Guid id, OrderService service) => service.Delivery(id));

        var channels = app.MapGroup("/api/admin/channels").WithTags("Channels").RequireAuthorization("Admin");
        channels.MapGet("/", (ChannelService service, int page = 1, int pageSize = 20, string? search = null) => service.List(page, pageSize, search));
        channels.MapGet("/{id:guid}", (Guid id, ChannelService service) => service.Get(id));
        channels.MapPost("/", async (CreateChannel request, HttpContext context, ChannelService service) => { var result = await service.Create(request, Actor(context)); return TypedResults.Created($"/api/admin/channels/{result.Id}", result); });
        channels.MapPatch("/{id:guid}", (Guid id, UpdateChannel request, HttpContext context, ChannelService service) => service.Update(id, request, Actor(context)));
        channels.MapPost("/{id:guid}/quota-adjustments", (Guid id, AdjustChannelQuota request, HttpContext context, ChannelService service) => service.AdjustQuota(id, request, Actor(context)));
        channels.MapGet("/{id:guid}/quota-ledger", (Guid id, ChannelService service, int page = 1, int pageSize = 20) => service.Ledger(id, page, pageSize));
        channels.MapPut("/{id:guid}/prices", (Guid id, SetChannelPrice request, HttpContext context, ChannelService service) => service.SetPrice(id, request, Actor(context)));

        var requests = app.MapGroup("/api/admin/portal-requests").WithTags("Customer requests").RequireAuthorization("Read", ApplicationPermissions.ManageOrders);
        requests.MapGet("/", (PortalRequestService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null, Guid? customerId = null, string? status = null) => service.List(page, pageSize, applicationId, channelId, customerId, status));
        requests.MapPost("/{id:guid}/review", (Guid id, ReviewPortalRequest request, HttpContext context, PortalRequestService service) => service.Review(id, request, Actor(context)));

        var credentials = app.MapGroup("/api/admin/api-credentials").WithTags("API credentials").RequireAuthorization("Admin");
        credentials.MapGet("/", (ApiCredentialService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null) => service.List(page, pageSize, applicationId, channelId));
        credentials.MapPost("/", async (CreateApiCredential request, HttpContext context, ApiCredentialService service) => { var result = await service.Create(request, Actor(context)); return TypedResults.Created($"/api/admin/api-credentials/{result.Credential.Id}", result); });
        credentials.MapPatch("/{id:guid}", (Guid id, UpdateApiCredential request, HttpContext context, ApiCredentialService service) => service.Update(id, request, Actor(context)));
        credentials.MapPost("/{id:guid}/rotate", (Guid id, HttpContext context, ApiCredentialService service) => service.Rotate(id, Actor(context)));
    }
}
