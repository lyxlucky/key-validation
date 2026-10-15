using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/admin/portal-requests")]
[Produces("application/json")]
[Tags("Customer requests")]
[Authorize(Policy = "Read")]
[Authorize(Policy = ApplicationPermissions.ManageOrders)]
public sealed class PortalRequestsController(PortalRequestService portalRequestService) : ControllerBase
{
    private readonly PortalRequestService _portalRequestService = portalRequestService;
    [HttpGet("")]
    public Task<PagedResult<PortalRequestDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null, [FromQuery] Guid? customerId = null, [FromQuery] string? status = null) => _portalRequestService.List(page, pageSize, applicationId, channelId, customerId, status);
    [HttpPost("{id:guid}/review")]
    public Task<PortalRequestDto> PostByIdReview([FromRoute] Guid id, [FromBody] ReviewPortalRequest request) => _portalRequestService.Review(id, request, RequestActor.FromContext(HttpContext));
}
