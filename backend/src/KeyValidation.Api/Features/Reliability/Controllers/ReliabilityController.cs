using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Reliability.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = "Read")]
[Tags("Operations reliability")]
public sealed class ReliabilityController(LicensingService licensingService, RiskService riskService, OperationsScanner operationsScanner, NotificationService notificationService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    private readonly RiskService _riskService = riskService;
    private readonly OperationsScanner _operationsScanner = operationsScanner;
    private readonly NotificationService _notificationService = notificationService;
    [HttpGet("catalog")]
    public Task<ReportsCatalogDto> GetCatalog() => _licensingService.OperationsCatalog();
    [HttpGet("applications/{id:guid}/risk-policy")]
    [Authorize(Policy = "Admin")]
    public Task<RiskPolicyDto> GetApplicationsByIdRiskPolicy([FromRoute] Guid id) => _riskService.Policy(id);
    [HttpPut("applications/{id:guid}/risk-policy")]
    [Authorize(Policy = "Admin")]
    public Task<RiskPolicyDto> PutApplicationsByIdRiskPolicy([FromRoute] Guid id, [FromBody] UpdateRiskPolicy request) => _riskService.SetPolicy(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("risk-events")]
    [Authorize(Policy = "risk.review")]
    public Task<PagedResult<RiskEventDto>> GetRiskEvents([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] string? status = null) => _riskService.List(page, pageSize, applicationId, status);
    [HttpGet("risk-events/{id:guid}")]
    [Authorize(Policy = "risk.review")]
    public Task<RiskEventDto> GetRiskEventsById([FromRoute] Guid id) => _riskService.Detail(id);
    [HttpPost("risk-events/{id:guid}/review")]
    [Authorize(Policy = "risk.review")]
    public Task<RiskEventDto> PostRiskEventsByIdReview([FromRoute] Guid id, [FromBody] ReviewRiskEvent request) => _riskService.Review(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("operations/scan")]
    [Authorize(Policy = "Admin")]
    public Task<OperationsScanDto> PostOperationsScan(CancellationToken cancellationToken) => _operationsScanner.ScanAsync(cancellationToken);
    [HttpGet("applications/{id:guid}/notification-policy")]
    [Authorize(Policy = "Admin")]
    public Task<NotificationPolicyDto> GetApplicationsByIdNotificationPolicy([FromRoute] Guid id) => _notificationService.Policy(id);
    [HttpPut("applications/{id:guid}/notification-policy")]
    [Authorize(Policy = "Admin")]
    public Task<NotificationPolicyDto> PutApplicationsByIdNotificationPolicy([FromRoute] Guid id, [FromBody] UpdateNotificationPolicy request) => _notificationService.SetPolicy(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("notifications")]
    public Task<PagedResult<NotificationDto>> GetNotifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool unreadOnly = false, [FromQuery] Guid? applicationId = null) => _notificationService.Inbox(RequestActor.FromContext(HttpContext).Id, page, pageSize, unreadOnly, applicationId);
    [HttpPost("notifications/{id:guid}/read")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostNotificationsByIdRead([FromRoute] Guid id)
    {
        await _notificationService.MarkRead(id, RequestActor.FromContext(HttpContext).Id);
        return NoContent();
    }
}
