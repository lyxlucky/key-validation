using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Reliability.Controllers;
[ApiController]
[Route("api/admin/backups")]
[Produces("application/json")]
[Authorize(Policy = "Admin")]
[Tags("Encrypted backups")]
public sealed class BackupsController(BackupService backupService) : ControllerBase
{
    private readonly BackupService _backupService = backupService;
    [HttpGet("status")]
    public Task<BackupStatusDto> GetStatus(CancellationToken cancellationToken) => _backupService.Status(cancellationToken);
    [HttpGet("")]
    public Task<PagedResult<BackupJobDto>> GetIndex(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _backupService.List(page, pageSize, cancellationToken);
    [HttpPost("")]
    [ProducesResponseType(typeof(BackupJobDto), 201)]
    public async Task<ActionResult<BackupJobDto>> PostIndex(CancellationToken cancellationToken)
    {
        var result = await _backupService.CreateAsync(RequestActor.FromContext(HttpContext), cancellationToken);
        return Created($"/api/admin/backups/{result.Id}", result);
    }

    [HttpPost("{id:guid}/verify")]
    public Task<BackupVerificationDto> PostByIdVerify([FromRoute] Guid id, CancellationToken cancellationToken) => _backupService.VerifyAsync(id, RequestActor.FromContext(HttpContext), cancellationToken);
}
