using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/licenses")]
[Produces("application/json")]
public sealed class LicenseQueriesController(LicensingService licensingService) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    [HttpPost("query")]
    [Tags("License management")]
    [Authorize(Policy = "Read")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<LicenseDto>> PostQuery([FromBody] LicenseQuery request) => _licensingService.QueryLicenses(request);
}
