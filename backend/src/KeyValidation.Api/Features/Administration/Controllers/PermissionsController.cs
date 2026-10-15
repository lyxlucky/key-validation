using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Administration.Controllers;
[ApiController]
[Route("api/admin/permissions")]
[Produces("application/json")]
public sealed class PermissionsController() : ControllerBase
{
    [HttpGet("")]
    [Tags("Roles")]
    [Authorize(Policy = ApplicationPermissions.Administrator)]
    public PermissionDto[] GetIndex() => ApplicationPermissions.Catalog;
}
