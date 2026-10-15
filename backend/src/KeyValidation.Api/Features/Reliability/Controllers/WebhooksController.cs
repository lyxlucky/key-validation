using KeyValidation.Application;
using KeyValidation.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace KeyValidation.Api.Features.Reliability.Controllers;
[ApiController]
[Route("api/admin")]
[Produces("application/json")]
[Authorize(Policy = ApplicationPermissions.ManageIntegrations)]
[Tags("Webhook notifications")]
public sealed class WebhooksController(WebhookService webhookService) : ControllerBase
{
    private readonly WebhookService _webhookService = webhookService;
    [HttpGet("webhook-subscriptions")]
    public Task<PagedResult<WebhookSubscriptionDto>> GetWebhookSubscriptions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? applicationId = null) => _webhookService.Subscriptions(page, pageSize, applicationId);
    [HttpPost("webhook-subscriptions")]
    [ProducesResponseType(typeof(WebhookSecretDto), 201)]
    public async Task<ActionResult<WebhookSecretDto>> PostWebhookSubscriptions([FromBody] CreateWebhookSubscription request)
    {
        var result = await _webhookService.Create(request, RequestActor.FromContext(HttpContext));
        return Created($"/api/admin/webhook-subscriptions/{result.Subscription.Id}", result);
    }

    [HttpPatch("webhook-subscriptions/{id:guid}")]
    public Task<WebhookSubscriptionDto> PatchWebhookSubscriptionsById([FromRoute] Guid id, [FromBody] UpdateWebhookSubscription request) => _webhookService.Update(id, request, RequestActor.FromContext(HttpContext));
    [HttpPost("webhook-subscriptions/{id:guid}/rotate-secret")]
    public Task<WebhookSecretDto> PostWebhookSubscriptionsByIdRotateSecret([FromRoute] Guid id) => _webhookService.Rotate(id, RequestActor.FromContext(HttpContext));
    [HttpGet("webhook-deliveries")]
    public Task<PagedResult<WebhookDeliveryDto>> GetWebhookDeliveries([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] Guid? subscriptionId = null, [FromQuery] string? status = null, [FromQuery] Guid? applicationId = null) => _webhookService.Deliveries(page, pageSize, subscriptionId, status, applicationId);
    [HttpPost("webhook-deliveries/{id:guid}/retry")]
    public Task<WebhookDeliveryDto> PostWebhookDeliveriesByIdRetry([FromRoute] Guid id) => _webhookService.Retry(id, RequestActor.FromContext(HttpContext));
}
