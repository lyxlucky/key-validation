using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.ClientExtensions.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Tags("Client extensions")]
[Authorize(Policy = "Read")]
public sealed class ClientExtensionsAdministrationController(ClientVersionPolicyService clientVersionPolicyService, OfflineLicenseService offlineLicenseService) : ControllerBase
{
    private readonly ClientVersionPolicyService _clientVersionPolicyService = clientVersionPolicyService;
    private readonly OfflineLicenseService _offlineLicenseService = offlineLicenseService;
    [HttpGet("applications/{id:guid}/version-policy")]
    public Task<VersionPolicyDto> GetApplicationsByIdVersionPolicy([FromRoute] Guid id) => _clientVersionPolicyService.Get(id);
    [HttpPut("applications/{id:guid}/version-policy")]
    [Authorize(Policy = "Admin")]
    public Task<VersionPolicyDto> PutApplicationsByIdVersionPolicy([FromRoute] Guid id, [FromBody] UpdateVersionPolicy request) => _clientVersionPolicyService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("offline-licenses/configuration")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public OfflineConfigurationDto GetOfflineLicensesConfiguration() => _offlineLicenseService.Configuration();
    [HttpGet("offline-licenses")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<OfflineLicenseDto>> GetOfflineLicenses([FromQuery] Guid? applicationId = null, [FromQuery] Guid? licenseId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _offlineLicenseService.List(applicationId, licenseId, page, pageSize);
    [HttpPost("offline-licenses")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(typeof(SignedOfflineLicenseDto), 201)]
    public async Task<ActionResult<SignedOfflineLicenseDto>> PostOfflineLicenses([FromBody] SignOfflineLicense request)
    {
        var result = await _offlineLicenseService.Sign(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/offline-licenses?licenseId={result.Record.LicenseId}", result);
    }

    [HttpPost("offline-licenses/{id:guid}/revoke")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostOfflineLicensesByIdRevoke([FromRoute] Guid id)
    {
        await _offlineLicenseService.Revoke(id, RequestActor.FromContext(HttpContext));
        return NoContent();
    }
}
