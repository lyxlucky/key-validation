using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/accounts")]
[Produces("application/json")]
[Tags("Accounts")]
[Authorize(Policy = "Admin")]
public sealed class AccountsController(AdminAccountService adminAccountService) : ControllerBase
{
    private readonly AdminAccountService _adminAccountService = adminAccountService;
    [HttpGet("")]
    public Task<PagedResult<AccountDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null) => _adminAccountService.List(page, pageSize, search);
    [HttpPost("")]
    [ProducesResponseType(typeof(AccountDto), 201)]
    public async Task<ActionResult<AccountDto>> PostIndex([FromBody] CreateAccountRequest request)
    {
        var account = await _adminAccountService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/accounts/{account.Id}", account);
    }

    [HttpPatch("{id}/role")]
    public Task<AccountDto> PatchByIdRole([FromRoute] string id, [FromBody] ChangeAccountRoleRequest request) => _adminAccountService.ChangeRole(id, request, RequestActor.FromContext(HttpContext));
    [HttpPatch("{id}/roles")]
    public Task<AccountDto> PatchByIdRoles([FromRoute] string id, [FromBody] ChangeAccountRolesRequest request) => _adminAccountService.ChangeRoles(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("{id}/disable")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostByIdDisable([FromRoute] string id)
    {
        await _adminAccountService.SetDisabled(id, true, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("{id}/enable")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostByIdEnable([FromRoute] string id)
    {
        await _adminAccountService.SetDisabled(id, false, RequestActor.FromContext(HttpContext));
        return NoContent();
    }

    [HttpPost("{id}/reset-password")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> PostByIdResetPassword([FromRoute] string id, [FromBody] ResetAccountPasswordRequest request)
    {
        await _adminAccountService.ResetPassword(id, request, RequestActor.FromContext(HttpContext));
        return NoContent();
    }
}
