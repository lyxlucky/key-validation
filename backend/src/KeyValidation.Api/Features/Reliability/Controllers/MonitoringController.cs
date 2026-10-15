using KeyValidation.Infrastructure;
using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Reliability.Controllers;
[ApiController]
[Route("api/admin/monitoring")]
[Produces("application/json")]
[Authorize(Policy = "Admin")]
[Tags("Service monitoring")]
public sealed class MonitoringController(MonitoringService monitoringService) : ControllerBase
{
    private readonly MonitoringService _monitoringService = monitoringService;
    [HttpGet("")]
    public Task<MonitoringDto> GetIndex(CancellationToken cancellationToken) => _monitoringService.GetAsync(cancellationToken);
    [HttpPost("sample")]
    public Task<MonitoringSampleDto> PostSample(CancellationToken cancellationToken) => _monitoringService.SampleAsync(cancellationToken);
    [HttpGet("metrics")]
    [Produces("text/plain")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMetrics(CancellationToken cancellationToken) => Content(await _monitoringService.PrometheusAsync(cancellationToken), "text/plain; version=0.0.4; charset=utf-8");
}
