using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;

namespace KeyValidation.Api.Features.ClientExtensions.Controllers;
[ApiController]
[Route("api/client/applications")]
[Produces("application/json")]
public sealed class ClientVersionsController(ClientVersionPolicyService clientVersionPolicyService) : ControllerBase
{
    private readonly ClientVersionPolicyService _clientVersionPolicyService = clientVersionPolicyService;
    [HttpGet("{appCode}/version")]
    [Tags("Software clients")]
    public Task<PublicVersionDto> GetByAppCodeVersion([FromRoute] string appCode) => _clientVersionPolicyService.Public(appCode);
}
