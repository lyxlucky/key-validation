using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;

namespace KeyValidation.Api.Features.ClientExtensions.Controllers;
[ApiController]
[Route("api/client/offline-licenses")]
[Produces("application/json")]
public sealed class OfflineLicenseVerificationController(OfflineLicenseService offlineLicenseService) : ControllerBase
{
    private readonly OfflineLicenseService _offlineLicenseService = offlineLicenseService;
    [HttpPost("verify")]
    [Tags("Software clients")]
    public Task<VerifiedOfflineLicenseDto> PostVerify([FromBody] VerifyOfflineLicense request) => _offlineLicenseService.Verify(request);
}
