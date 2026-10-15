using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Plans.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Tags("Plan catalog")]
[Authorize(Policy = "Read")]
public sealed class PlansController(PlanCatalogService planCatalogService, LicensingService licensingService) : ControllerBase
{
    private readonly PlanCatalogService _planCatalogService = planCatalogService;
    private readonly LicensingService _licensingService = licensingService;
    [HttpGet("plans")]
    [Authorize(Policy = "Admin")]
    public Task<PagedResult<GlobalPlanDto>> GetPlans([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null, [FromQuery] bool? isEnabled = null) => _planCatalogService.List(page, pageSize, search, isEnabled);
    [HttpGet("plans/{id:guid}")]
    [Authorize(Policy = "Admin")]
    public Task<GlobalPlanDto> GetPlansById([FromRoute] Guid id) => _planCatalogService.Get(id);
    [HttpPost("plans")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(GlobalPlanDto), 201)]
    public async Task<ActionResult<GlobalPlanDto>> PostPlans([FromBody] CreatePlan request)
    {
        var result = await _planCatalogService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/plans/{result.Id}", result);
    }

    [HttpPatch("plans/{id:guid}")]
    [Authorize(Policy = "Admin")]
    public Task<GlobalPlanDto> PatchPlansById([FromRoute] Guid id, [FromBody] UpdatePlan request) => _planCatalogService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("plans/{id:guid}/applications")]
    [Authorize(Policy = "Admin")]
    public Task<IReadOnlyList<PlanReferenceDto>> GetPlansByIdApplications([FromRoute] Guid id) => _planCatalogService.Applications(id);
    [HttpGet("applications/{id:guid}/plan-references")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<ApplicationPlanDto>> GetApplicationsByIdPlanReferences([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _planCatalogService.References(id, page, pageSize);
    [HttpPut("applications/{id:guid}/plan-references/{planId:guid}")]
    [Authorize(Policy = "Admin")]
    public Task<ApplicationPlanDto> PutApplicationsByIdPlanReferencesByPlanId([FromRoute] Guid id, [FromRoute] Guid planId, [FromBody] SetPlanReference request) => _planCatalogService.SetReference(id, planId, request, RequestActor.FromContext(HttpContext));
    [HttpGet("applications/{id:guid}/plans")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<PagedResult<PlanDto>> GetApplicationsByIdPlans([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _licensingService.Plans(id, page, pageSize);
    [HttpPost("applications/{id:guid}/plans")]
    [Authorize(Policy = "Admin")]
    [ProducesResponseType(typeof(PlanDto), 201)]
    public async Task<ActionResult<PlanDto>> PostApplicationsByIdPlans([FromRoute] Guid id, [FromBody] CreatePlan request)
    {
        var result = await _planCatalogService.CreateForApplication(id, request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/plans/{result.Id}", result);
    }
}
