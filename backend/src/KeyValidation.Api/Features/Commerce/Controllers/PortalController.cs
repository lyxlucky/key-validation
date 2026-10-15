using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/portal")]
[Produces("application/json")]
[Tags("Customer portal")]
public sealed class PortalController(CommerceAuthentication commerceAuthentication, CommerceIdentity commerceIdentity, CustomerService customerService, OrderService orderService, LicensingService licensingService, PortalRequestService portalRequestService) : ControllerBase
{
    private readonly CommerceAuthentication _commerceAuthentication = commerceAuthentication;
    private readonly CommerceIdentity _commerceIdentity = commerceIdentity;
    private readonly CustomerService _customerService = customerService;
    private readonly OrderService _orderService = orderService;
    private readonly LicensingService _licensingService = licensingService;
    private readonly PortalRequestService _portalRequestService = portalRequestService;
    [HttpPost("sessions")]
    [EnableRateLimiting("login")]
    public Task<PortalSessionDto> PostSessions([FromBody] CreatePortalSession request) => _commerceAuthentication.Login(request);
    [HttpDelete("sessions")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteSessions()
    {
        await _commerceAuthentication.Logout();
        return NoContent();
    }

    [HttpGet("me")]
    public Task<CustomerDto> GetMe() => _customerService.Get(_commerceIdentity.CustomerId!.Value);
    [HttpGet("orders")]
    public Task<PagedResult<OrderDto>> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _orderService.List(page, pageSize, null, null, null, null);
    [HttpGet("orders/{id:guid}")]
    public Task<OrderDto> GetOrdersById([FromRoute] Guid id) => _orderService.Get(id);
    [HttpGet("orders/{id:guid}/delivery")]
    public Task<OrderDeliveryDto> GetOrdersByIdDelivery([FromRoute] Guid id) => _orderService.Delivery(id);
    [HttpGet("licenses")]
    public Task<PagedResult<LicenseDto>> GetLicenses([FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _licensingService.Licenses(page, pageSize, null, null, null, null, null);
    [HttpGet("licenses/{id:guid}")]
    public Task<LicenseDetail> GetLicensesById([FromRoute] Guid id) => _licensingService.Detail(id);
    [HttpGet("requests")]
    public Task<PagedResult<PortalRequestDto>> GetRequests([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? status = null) => _portalRequestService.List(page, pageSize, null, null, null, status);
    [HttpPost("requests")]
    [ProducesResponseType(typeof(PortalRequestDto), 201)]
    public async Task<ActionResult<PortalRequestDto>> PostRequests([FromBody] CreatePortalRequest request)
    {
        var result = await _portalRequestService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/portal/requests/{result.Id}", result);
    }
}
