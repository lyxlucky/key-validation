using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/reports")]
[Produces("application/json")]
public sealed class ReportsController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpGet("operations")]
    [Tags("Operations reports")]
    [Authorize(Policy = "Read")]
    [Authorize(Policy = ApplicationPermissions.ReadReports)]
    public Task<OperationsReport> GetOperations([FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? planId = null, [FromQuery] Guid? channelId = null) => _licensingService.OperationsReport(from, to, applicationId, planId, channelId);
    [HttpGet("catalog")]
    [Tags("Operations reports")]
    [Authorize(Policy = "Read")]
    [Authorize(Policy = ApplicationPermissions.ReadReports)]
    public Task<ReportsCatalogDto> GetCatalog() => _licensingService.ReportsCatalog();
}
