using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;

namespace KeyValidation.Api.DeveloperIntegration.Controllers;
[ApiController]
[Route("api/integrations/developer-docs")]
[Produces("application/json")]
public sealed class DeveloperDocumentationController(DeveloperDocumentationCatalog developerDocumentationCatalog) : ControllerBase
{
    private readonly DeveloperDocumentationCatalog _developerDocumentationCatalog = developerDocumentationCatalog;
    [HttpGet("")]
    [Tags("Developer documentation")]
    [AllowAnonymous]
    [EnableRateLimiting("developer-docs")]
    [ProducesResponseType(typeof(IReadOnlyList<DeveloperDocumentSummary>), 200)]
    public ActionResult<IReadOnlyList<DeveloperDocumentSummary>> GetIndex() => Ok(_developerDocumentationCatalog.List());
    [HttpGet("{id}")]
    [Tags("Developer documentation")]
    [AllowAnonymous]
    [EnableRateLimiting("developer-docs")]
    public IActionResult GetById([FromRoute] string id) => _developerDocumentationCatalog.Get(id)is { } document ? Ok(document) : NotFound();
}
