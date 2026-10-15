using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/accounts")]
[Produces("application/json")]
[Tags("Account access")]
[Authorize(Policy = "Admin")]
public sealed class AccountAccessController(AdminAccountService adminAccountService) : ControllerBase
{
    private readonly AdminAccountService _adminAccountService = adminAccountService;
    [HttpGet("{id}/access")]
    public Task<AccountAccessDto> GetByIdAccess([FromRoute] string id) => _adminAccountService.GetAccess(id);
    [HttpPut("{id}/access")]
    public Task<AccountAccessDto> PutByIdAccess([FromRoute] string id, [FromBody] UpdateAccountAccess request) => _adminAccountService.SetAccess(id, request, new(HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), HttpContext.TraceIdentifier));
}
