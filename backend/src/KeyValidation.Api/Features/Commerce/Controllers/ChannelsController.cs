using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Commerce.Controllers;
[ApiController]
[Route("api/admin/channels")]
[Produces("application/json")]
[Tags("Channels")]
[Authorize(Policy = "Admin")]
public sealed class ChannelsController(ChannelService channelService) : ControllerBase
{
    private readonly ChannelService _channelService = channelService;
    [HttpGet("")]
    public Task<PagedResult<ChannelDto>> GetIndex([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? search = null) => _channelService.List(page, pageSize, search);
    [HttpGet("{id:guid}")]
    public Task<ChannelDto> GetById([FromRoute] Guid id) => _channelService.Get(id);
    [HttpPost("")]
    [ProducesResponseType(typeof(ChannelDto), 201)]
    public async Task<ActionResult<ChannelDto>> PostIndex([FromBody] CreateChannel request)
    {
        var result = await _channelService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/channels/{result.Id}", result);
    }

    [HttpPatch("{id:guid}")]
    public Task<ChannelDto> PatchById([FromRoute] Guid id, [FromBody] UpdateChannel request) => _channelService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("{id:guid}/quota-adjustments")]
    public Task<ChannelDto> PostByIdQuotaAdjustments([FromRoute] Guid id, [FromBody] AdjustChannelQuota request) => _channelService.AdjustQuota(id, request, RequestActor.FromContext(HttpContext));
    [HttpGet("{id:guid}/quota-ledger")]
    public Task<PagedResult<ChannelQuotaDto>> GetByIdQuotaLedger([FromRoute] Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 20) => _channelService.Ledger(id, page, pageSize);
    [HttpPut("{id:guid}/prices")]
    public Task<ChannelPriceDto> PutByIdPrices([FromRoute] Guid id, [FromBody] SetChannelPrice request) => _channelService.SetPrice(id, request, RequestActor.FromContext(HttpContext));
}
