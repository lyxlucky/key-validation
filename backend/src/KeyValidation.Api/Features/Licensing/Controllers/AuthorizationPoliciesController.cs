using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Tags("Authorization policies")]
[Authorize(Policy = "Read")]
public sealed class AuthorizationPoliciesController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpPost("licenses/{id:guid}/freeze")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLicensesByIdFreeze([FromRoute] Guid id, [FromBody] FreezeLicense request)
    {
        await _licensingService.FreezeLicense(id, request, true, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("licenses/{id:guid}/unfreeze")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostLicensesByIdUnfreeze([FromRoute] Guid id, [FromBody] FreezeLicense request)
    {
        await _licensingService.FreezeLicense(id, request, false, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpGet("licenses/{id:guid}/freeze-history")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<LicenseFreezeDto>> GetLicensesByIdFreezeHistory([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _licensingService.FreezeHistory(id, page, pageSize);
    [HttpGet("applications/{id:guid}/trial-policy")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<TrialPolicyDto> GetApplicationsByIdTrialPolicy([FromRoute] Guid id) => _licensingService.TrialPolicy(id);
    [HttpPut("applications/{id:guid}/trial-policy")]
    [Authorize(Policy = "Admin")]
    public Task<TrialPolicyDto> PutApplicationsByIdTrialPolicy([FromRoute] Guid id, [FromBody] UpdateTrialPolicy request) => _licensingService.UpdateTrialPolicy(id, request, RequestActor.FromContext(HttpContext));
}
