using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/admin/api-credentials")]
[Produces("application/json")]
[Tags("API credentials")]
[Authorize(Policy = "Admin")]
public sealed class ApiCredentialsController(ApiCredentialService apiCredentialService) : ControllerBase
{
    private readonly ApiCredentialService _apiCredentialService = apiCredentialService;
    [HttpGet("")]
    public Task<PagedResult<ApiCredentialDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null) => _apiCredentialService.List(page, pageSize, applicationId, channelId);
    [HttpPost("")]
    [ProducesResponseType(typeof(ApiCredentialSecretDto), 201)]
    public async Task<ActionResult<ApiCredentialSecretDto>> PostIndex([FromBody] CreateApiCredential request)
    {
        var result = await _apiCredentialService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/api-credentials/{result.Credential.Id}", result);
    }

    [HttpPatch("{id:guid}")]
    public Task<ApiCredentialDto> PatchById([FromRoute] Guid id, [FromBody] UpdateApiCredential request) => _apiCredentialService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/rotate")]
    public Task<ApiCredentialSecretDto> PostByIdRotate([FromRoute] Guid id) => _apiCredentialService.Rotate(id, RequestActor.FromContext(HttpContext));
}
