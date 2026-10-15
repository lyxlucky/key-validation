using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Licensing.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Tags("License management")]
[Authorize(Policy = "Read")]
public sealed class LicenseManagementController(LicensingService licensingService, AppDbContext appDbContext) : ControllerBase
{
    private readonly LicensingService _licensingService = licensingService;
    private readonly AppDbContext _appDbContext = appDbContext;
    [HttpGet("batch-overview")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public async Task<PagedResult<BatchOverviewDto>> GetBatchOverview([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null, [FromQuery] Guid? planId = null, [FromQuery] string? createdBy = null, [FromQuery] string? search = null, [FromQuery] Guid? channelId = null)
    {
        var result = await _licensingService.BatchOverview(page, pageSize, applicationId, planId, createdBy, search, channelId);
        var ids = result.Items.Select(x => x.CreatedBy).Distinct().ToArray();
        var names = await _appDbContext.Users.AsNoTracking().Where(x => ids.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.UserName);
        return result with
        {
            Items = result.Items.Select(x => x with { CreatedByName = names.GetValueOrDefault(x.CreatedBy) }).ToArray()
        };
    }

    [HttpGet("batch-overview/{id:guid}")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public async Task<BatchOverviewDto> GetBatchOverviewById([FromRoute] Guid id)
    {
        var result = await _licensingService.BatchOverviewDetail(id);
        var name = await _appDbContext.Users.AsNoTracking().Where(x => x.Id == result.CreatedBy).Select(x => x.UserName).SingleOrDefaultAsync();
        return result with
        {
            CreatedByName = name
        };
    }

    [HttpGet("expiring-licenses")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<ExpiringLicensesDto> GetExpiringLicenses([FromQuery] int withinDays = 7, [FromQuery] Guid? applicationId = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? channelId = null) => _licensingService.ExpiringLicenses(page, pageSize, withinDays, applicationId, channelId);
    [HttpPost("license-operations")]
    [Authorize(Policy = "Operate")]
    [Authorize(Policy = ApplicationPermissions.ManageLicenses)]
    public Task<OperationResult> PostLicenseOperations([FromBody] LicenseOperationRequest request) => _licensingService.LicenseOperation(request, RequestActor.FromContext(HttpContext));
    [HttpGet("license-operations/{requestId:guid}")]
    [Authorize(Policy = ApplicationPermissions.ReadLicenses)]
    public Task<OperationResult> GetLicenseOperationsByRequestId([FromRoute] Guid requestId) => _licensingService.LicenseOperationReceipt(requestId, RequestActor.FromContext(HttpContext));
}
