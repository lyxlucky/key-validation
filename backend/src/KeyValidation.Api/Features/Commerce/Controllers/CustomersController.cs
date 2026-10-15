using KeyValidation.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/admin/customers")]
[Produces("application/json")]
[Tags("Customers")]
[Authorize(Policy = "Read")]
[Authorize(Policy = ApplicationPermissions.ManageCustomers)]
public sealed class CustomersController(CustomerService customerService) : ControllerBase
{
    private readonly CustomerService _customerService = customerService;
    [HttpGet("")]
    public Task<PagedResult<CustomerDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? channelId = null, [FromQuery] string? search = null) => _customerService.List(page, pageSize, applicationId, channelId, search);
    [HttpGet("{id:guid}")]
    public Task<CustomerDto> GetById([FromRoute] Guid id) => _customerService.Get(id);
    [HttpPost("")]
    [ProducesResponseType(typeof(CustomerDto), 201)]
    public async Task<ActionResult<CustomerDto>> PostIndex([FromBody] CreateCustomer request)
    {
        var result = await _customerService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/customers/{result.Id}", result);
    }

    [HttpPatch("{id:guid}")]
    public Task<CustomerDto> PatchById([FromRoute] Guid id, [FromBody] UpdateCustomer request) => _customerService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/disable")]
    public Task<CustomerDto> PostByIdDisable([FromRoute] Guid id) => _customerService.SetAccess(id, "disable", RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/enable")]
    public Task<CustomerDto> PostByIdEnable([FromRoute] Guid id) => _customerService.SetAccess(id, "enable", RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/anonymize")]
    public Task<CustomerDto> PostByIdAnonymize([FromRoute] Guid id) => _customerService.SetAccess(id, "anonymize", RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/rotate-access-code")]
    public Task<CustomerAccessCodeDto> PostByIdRotateAccessCode([FromRoute] Guid id) => _customerService.RotateAccessCode(id, RequestActor.FromContext(HttpContext));
    [HttpGet("{id:guid}/licenses")]
    public Task<PagedResult<LicenseDto>> GetByIdLicenses([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _customerService.Licenses(id, page, pageSize);
}
