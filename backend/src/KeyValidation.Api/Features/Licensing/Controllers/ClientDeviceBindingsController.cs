using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/client/device-bindings")]
[Produces("application/json")]
public sealed class ClientDeviceBindingsController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpPost("release")]
    [Tags("Software clients")]
    [EnableRateLimiting("heartbeat")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostRelease()
    {
        var value = HttpContext.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || value.Length is < 40 or > 256)
            throw new RuleException("SESSION_INVALID", 401);
        await _licensingService.ReleaseCurrentBinding(value[7..]);
        return NoContent();
    }
}
