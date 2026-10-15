using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/admin/orders")]
[Produces("application/json")]
[Tags("Orders")]
[Authorize(Policy = "Read")]
[Authorize(Policy = ApplicationPermissions.ManageOrders)]
public sealed class OrdersController(OrderService orderService) : ControllerBase
{
    private readonly OrderService _orderService = orderService;
    [HttpGet("")]
    public Task<PagedResult<OrderDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null, [FromQuery] Guid? customerId = null, [FromQuery] string? status = null, [FromQuery] Guid? requestId = null) => _orderService.List(page, pageSize, applicationId, channelId, customerId, status, requestId);
    [HttpGet("catalog")]
    public Task<OrderCatalogDto> GetCatalog([FromQuery] Guid? customerId = null) => _orderService.Catalog(customerId);
    [HttpGet("{id:guid}")]
    public Task<OrderDto> GetById([FromRoute] Guid id) => _orderService.Get(id);
    [HttpPost("")]
    [ProducesResponseType(typeof(OrderDto), 201)]
    public async Task<ActionResult<OrderDto>> PostIndex([FromBody] CreateOrder request)
    {
        var result = await _orderService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/orders/{result.Id}", result);
    }

    [HttpPost("{id:guid}/fulfill")]
    public Task<OrderDto> PostByIdFulfill([FromRoute] Guid id, [FromBody] FulfillOrder request) => _orderService.Fulfill(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/cancel")]
    public Task<OrderDto> PostByIdCancel([FromRoute] Guid id) => _orderService.Cancel(id, RequestActor.FromContext(HttpContext));
    [HttpGet("{id:guid}/delivery")]
    public Task<OrderDeliveryDto> GetByIdDelivery([FromRoute] Guid id) => _orderService.Delivery(id);
}
