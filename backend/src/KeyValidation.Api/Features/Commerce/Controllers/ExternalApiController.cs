using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Tags("External API")]
public sealed class ExternalApiController(LicensingService licensingService, CustomerService customerService, OrderService orderService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    private readonly CustomerService _customerService = customerService;
    private readonly OrderService _orderService = orderService;
    [HttpGet("catalog")]
    public Task<ReportsCatalogDto> GetCatalog() => _licensingService.OperationsCatalog();
    [HttpGet("licenses")]
    public Task<PagedResult<LicenseDto>> GetLicenses([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? batchId = null, [FromQuery] string? status = null, [FromQuery] Guid? channelId = null) => _licensingService.Licenses(page, pageSize, applicationId, batchId, status, null, null, channelId: channelId);
    [HttpGet("licenses/{id:guid}")]
    public Task<LicenseDetail> GetLicensesById([FromRoute] Guid id) => _licensingService.Detail(id);
    [HttpGet("customers")]
    public Task<PagedResult<CustomerDto>> GetCustomers([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null, [FromQuery] string? search = null) => _customerService.List(page, pageSize, applicationId, channelId, search);
    [HttpGet("customers/{id:guid}")]
    public Task<CustomerDto> GetCustomersById([FromRoute] Guid id) => _customerService.Get(id);
    [HttpGet("customers/{id:guid}/licenses")]
    public Task<PagedResult<LicenseDto>> GetCustomersByIdLicenses([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _customerService.Licenses(id, page, pageSize);
    [HttpPost("customers")]
    [ProducesResponseType(typeof(CustomerDto), 201)]
    public async Task<ActionResult<CustomerDto>> PostCustomers([FromBody] CreateCustomer request)
    {
        var result = await _customerService.Create(request, RequestActor.FromContext(HttpContext), requireRequestId: true);
        return Created($"/api/v1/customers/{result.Id}", result);
    }

    [HttpPatch("customers/{id:guid}")]
    public Task<CustomerDto> PatchCustomersById([FromRoute] Guid id, [FromBody] ExternalUpdateCustomer request) => _customerService.Update(id, new(request.Name, request.Email, request.Phone, request.Note), RequestActor.FromContext(HttpContext), request.RequestId, true);
    [HttpGet("orders")]
    public Task<PagedResult<OrderDto>> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null, [FromQuery] Guid? customerId = null, [FromQuery] string? status = null, [FromQuery] Guid? requestId = null) => _orderService.List(page, pageSize, applicationId, channelId, customerId, status, requestId);
    [HttpGet("orders/catalog")]
    public Task<OrderCatalogDto> GetOrdersCatalog([FromQuery] Guid? customerId = null) => _orderService.Catalog(customerId);
    [HttpGet("orders/{id:guid}")]
    public Task<OrderDto> GetOrdersById([FromRoute] Guid id) => _orderService.Get(id);
    [HttpPost("orders")]
    [ProducesResponseType(typeof(OrderDto), 201)]
    public async Task<ActionResult<OrderDto>> PostOrders([FromBody] CreateOrder request)
    {
        var result = await _orderService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/v1/orders/{result.Id}", result);
    }

    [HttpPost("orders/{id:guid}/fulfill")]
    public Task<OrderDto> PostOrdersByIdFulfill([FromRoute] Guid id, [FromBody] FulfillOrder request) => _orderService.Fulfill(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("orders/{id:guid}/delivery")]
    public Task<OrderDeliveryDto> GetOrdersByIdDelivery([FromRoute] Guid id) => _orderService.Delivery(id);
}
