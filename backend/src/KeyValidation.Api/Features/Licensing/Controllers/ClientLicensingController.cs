using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/client")]
[Produces("application/json")]
[Tags("Software clients")]
public sealed class ClientLicensingController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpPost("licenses/activate")]
    [ServiceFilter(typeof(ActivationAdmissionActionFilter))]
    public async Task<ActivationDto> PostLicensesActivate([FromBody] ActivateLicense request)
    {
        var result = await _licensingService.Activate(request);
        ApiMetrics.Activations.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
        return result;
    }

    [HttpPost("sessions/heartbeat")]
    [EnableRateLimiting("heartbeat")]
    public Task<HeartbeatDto> PostSessionsHeartbeat() => _licensingService.Heartbeat(Token(HttpContext), HttpContext.Request.Headers.TryGetValue("X-Client-Version", out var version) ? version.ToString() : null);
    [HttpPost("sessions/logout")]
    [EnableRateLimiting("heartbeat")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostSessionsLogout()
    {
        await _licensingService.LogoutClient(Token(HttpContext));
        return NoContent();
    }

    private static string Token(HttpContext context)
    {
        var value = context.Request.Headers.Authorization.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || value.Length is < 40 or > 256)
            throw new RuleException("SESSION_INVALID", 401);
        return value[7..];
    }
}
