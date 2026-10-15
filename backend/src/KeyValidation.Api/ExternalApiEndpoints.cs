using KeyValidation.Application;

namespace KeyValidation.Api;

public static class ExternalApiEndpoints
{
    public static void MapExternalApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/v1").WithTags("External API");
        api.MapGet("/catalog", (LicensingService service) => service.OperationsCatalog());
        api.MapGet("/licenses", (LicensingService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? batchId = null, string? status = null, Guid? channelId = null) => service.Licenses(page, pageSize, applicationId, batchId, status, null, null, channelId: channelId));
        api.MapGet("/licenses/{id:guid}", (Guid id, LicensingService service) => service.Detail(id));
        api.MapGet("/customers", (CustomerService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null, string? search = null) => service.List(page, pageSize, applicationId, channelId, search));
        api.MapGet("/customers/{id:guid}", (Guid id, CustomerService service) => service.Get(id));
        api.MapGet("/customers/{id:guid}/licenses", (Guid id, CustomerService service, int page = 1, int pageSize = 20) => service.Licenses(id, page, pageSize));
        api.MapPost("/customers", async (CreateCustomer request, HttpContext context, CustomerService service) =>
        { var result = await service.Create(request, CommerceEndpoints.Actor(context), requireRequestId: true); return TypedResults.Created($"/api/v1/customers/{result.Id}", result); });
        api.MapPatch("/customers/{id:guid}", (Guid id, ExternalUpdateCustomer request, HttpContext context, CustomerService service) => service.Update(id, new(request.Name, request.Email, request.Phone, request.Note), CommerceEndpoints.Actor(context), request.RequestId, true));
        api.MapGet("/orders", (OrderService service, int page = 1, int pageSize = 20, Guid? applicationId = null, Guid? channelId = null, Guid? customerId = null, string? status = null, Guid? requestId = null) => service.List(page, pageSize, applicationId, channelId, customerId, status, requestId));
        api.MapGet("/orders/catalog", (OrderService service, Guid? customerId = null) => service.Catalog(customerId));
        api.MapGet("/orders/{id:guid}", (Guid id, OrderService service) => service.Get(id));
        api.MapPost("/orders", async (CreateOrder request, HttpContext context, OrderService service) =>
        { var result = await service.Create(request, CommerceEndpoints.Actor(context)); return TypedResults.Created($"/api/v1/orders/{result.Id}", result); });
        api.MapPost("/orders/{id:guid}/fulfill", (Guid id, FulfillOrder request, HttpContext context, OrderService service) => service.Fulfill(id, request, CommerceEndpoints.Actor(context)));
        api.MapGet("/orders/{id:guid}/delivery", (Guid id, OrderService service) => service.Delivery(id));
    }
}
