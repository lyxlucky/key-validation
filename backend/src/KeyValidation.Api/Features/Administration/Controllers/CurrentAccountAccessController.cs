using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/auth")]
[Produces("application/json")]
public sealed class CurrentAccountAccessController(ApplicationAccessScope applicationAccessScope) : ControllerBase
{
    private readonly ApplicationAccessScope _applicationAccessScope = applicationAccessScope;
    [HttpGet("access")]
    [Tags("Account access")]
    [Authorize(Policy = "Read")]
    public AccountAccessDto GetAccess() => _applicationAccessScope.ToDto();
}
