using KeyValidation.Application;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class PortalEndpoints
{
    public static void MapPortalApi(this WebApplication app)
    {
        var portal = app.MapGroup("/api/portal").WithTags("Customer portal");
        portal.MapPost("/sessions", (CreatePortalSession request, CommerceAuthentication service) => service.Login(request)).RequireRateLimiting("login");
        portal.MapDelete("/sessions", async (CommerceAuthentication service) => { await service.Logout(); return TypedResults.NoContent(); });
        portal.MapGet("/me", (CommerceIdentity identity, CustomerService customers) => customers.Get(identity.CustomerId!.Value));
        portal.MapGet("/orders", (OrderService orders, int page = 1, int pageSize = 20) => orders.List(page, pageSize, null, null, null, null));
        portal.MapGet("/orders/{id:guid}", (Guid id, OrderService orders) => orders.Get(id));
        portal.MapGet("/orders/{id:guid}/delivery", (Guid id, OrderService orders) => orders.Delivery(id));
        portal.MapGet("/licenses", (LicensingService licensing, int page = 1, int pageSize = 20) => licensing.Licenses(page, pageSize, null, null, null, null, null));
        portal.MapGet("/licenses/{id:guid}", (Guid id, LicensingService licensing) => licensing.Detail(id));
        portal.MapGet("/requests", (PortalRequestService requests, int page = 1, int pageSize = 20, string? status = null) => requests.List(page, pageSize, null, null, null, status));
        portal.MapPost("/requests", async (CreatePortalRequest request, HttpContext context, PortalRequestService requests) =>
        { var result = await requests.Create(request, CommerceEndpoints.Actor(context)); return TypedResults.Created($"/api/portal/requests/{result.Id}", result); });
    }
}
