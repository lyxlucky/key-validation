using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/roles")]
[Produces("application/json")]
[Tags("Roles")]
[Authorize(Policy = ApplicationPermissions.Administrator)]
public sealed class RolesController(AdminAccountService adminAccountService) : ControllerBase
{
    private readonly AdminAccountService _adminAccountService = adminAccountService;
    [HttpGet("")]
    public Task<PagedResult<RoleDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null) => _adminAccountService.ListRoles(page, pageSize, search);
    [HttpGet("{id}")]
    public Task<RoleDto> GetById([FromRoute] string id) => _adminAccountService.GetRole(id);
    [HttpPost("")]
    [ProducesResponseType(typeof(RoleDto), 201)]
    public async Task<ActionResult<RoleDto>> PostIndex([FromBody] SaveRoleRequest request)
    {
        var role = await _adminAccountService.CreateRole(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/roles/{role.Id}", role);
    }

    [HttpPatch("{id}")]
    public Task<RoleDto> PatchById([FromRoute] string id, [FromBody] SaveRoleRequest request) => _adminAccountService.UpdateRole(id, request, RequestActor.FromContext(HttpContext));
    [HttpDelete("{id}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> DeleteById([FromRoute] string id)
    {
        await _adminAccountService.DeleteRole(id, RequestActor.FromContext(HttpContext));
        return NoContent();
    }
}
