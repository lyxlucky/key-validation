using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/admin/licenses/{id:guid}/device-policy")]
[Produces("application/json")]
[Tags("Device rebinding")]
[Authorize(Policy = "Read")]
public sealed class DevicePoliciesController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpGet("")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<DevicePolicyDto> GetIndex([FromRoute] Guid id) => _licensingService.DevicePolicy(id);
    [HttpPatch("")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    public Task<DevicePolicyDto> PatchIndex([FromRoute] Guid id, [FromBody] UpdateDevicePolicy request) => _licensingService.UpdateDevicePolicy(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("reset")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    public Task<DevicePolicyDto> PostReset([FromRoute] Guid id, [FromBody] ResetDevicePolicy request) => _licensingService.ResetDevicePolicy(id, request, RequestActor.FromContext(HttpContext));
}
