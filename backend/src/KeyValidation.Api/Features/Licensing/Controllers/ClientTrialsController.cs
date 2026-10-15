using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/client/trials")]
[Produces("application/json")]
public sealed class ClientTrialsController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpPost("activate")]
    [Tags("Software clients")]
    [ServiceFilter(typeof(ActivationAdmissionActionFilter))]
    public async Task<ActivationDto> PostActivate([FromBody] ActivateTrial request)
    {
        var result = await _licensingService.ActivateTrial(request);
        ApiMetrics.Activations.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
        return result;
    }
}
