using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Tags("Administration")]
[Authorize(Policy = "Read")]
public sealed class LicensingAdministrationController(LicensingService licensingService, AuditLogService auditLogService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    private readonly AuditLogService _auditLogService = auditLogService;
    [HttpGet("dashboard")]
    [Authorize(Policy = ApplicationPermissions.ReadReports)]
    public Task<DashboardDto> GetDashboard([FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null) => _licensingService.Dashboard(applicationId, channelId);
    [HttpGet("applications")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<ApplicationDto>> GetApplications([FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _licensingService.Applications(page, pageSize);
    [HttpPost("applications")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(ApplicationDto), 201)]
    public async Task<ActionResult<ApplicationDto>> PostApplications([FromBody] CreateApplication request)
    {
        var result = await _licensingService.CreateApplication(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/applications/{result.Id}", result);
    }

    [HttpPost("applications/{id:guid}/disable")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostApplicationsByIdDisable([FromRoute] Guid id)
    {
        await _licensingService.SetApplication(id, true, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("applications/{id:guid}/enable")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostApplicationsByIdEnable([FromRoute] Guid id)
    {
        await _licensingService.SetApplication(id, false, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("license-batches")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.IssueLicenses)]
    [ProducesResponseType(typeof(IssuanceDto), 201)]
    public async Task<ActionResult<IssuanceDto>> PostLicenseBatches([FromBody] IssueBatch request)
    {
        var result = await _licensingService.Issue(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/license-batches?requestId={result.Batch.RequestId}", result);
    }

    [HttpGet("license-batches")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<BatchDto>> GetLicenseBatches([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? requestId = null, [FromQuery] Guid? channelId = null) => _licensingService.Batches(page, pageSize, applicationId, requestId, RequestActor.FromContext(HttpContext), channelId);
    [HttpPost("license-batches/{id:guid}/disable")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLicenseBatchesByIdDisable([FromRoute] Guid id)
    {
        await _licensingService.DisableBatch(id, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpGet("licenses")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<LicenseDto>> GetLicenses([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? batchId = null, [FromQuery] string? status = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] string? note = null, [FromQuery] string? tag = null, [FromQuery] Guid? channelId = null) => _licensingService.Licenses(page, pageSize, applicationId, batchId, status, from, to, note, tag, channelId);
    [HttpPost("licenses/search")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<LicenseDetail> PostLicensesSearch([FromBody] SearchLicense request) => _licensingService.Search(request.LicenseKey);
    [HttpGet("licenses/{id:guid}")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<LicenseDetail> GetLicensesById([FromRoute] Guid id) => _licensingService.Detail(id);
    [HttpPost("licenses/{id:guid}/disable")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLicensesByIdDisable([FromRoute] Guid id)
    {
        await _licensingService.SetLicense(id, true, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("licenses/{id:guid}/enable")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLicensesByIdEnable([FromRoute] Guid id)
    {
        await _licensingService.SetLicense(id, false, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("device-bindings/{id:guid}/release")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostDeviceBindingsByIdRelease([FromRoute] Guid id)
    {
        await _licensingService.ReleaseBinding(id, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("sessions/{id:guid}/revoke")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostSessionsByIdRevoke([FromRoute] Guid id)
    {
        await _licensingService.RevokeSession(id, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpGet("audit-logs")]
    [Authorize(Policy = ApplicationPermissions.ReadAudit)]
    public Task<PagedResult<AuditDto>> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? actorId = null, [FromQuery] Guid? targetId = null, [FromQuery] string? action = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] string? category = null, [FromQuery] string? actorName = null) => _auditLogService.List(page, pageSize, actorId, targetId, action, from, to, category, actorName);
    [HttpGet("login-logs")]
    [Authorize(Policy = ApplicationPermissions.ReadAudit)]
    public Task<PagedResult<AuditDto>> GetLoginLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? actorId = null, [FromQuery] Guid? targetId = null, [FromQuery] string? action = null, [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null, [FromQuery] string? actorName = null) => _auditLogService.List(page, pageSize, actorId, targetId, action, from, to, "login", actorName);
}
