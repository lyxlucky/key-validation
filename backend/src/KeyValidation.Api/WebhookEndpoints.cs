using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class WebhookEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    public static void MapWebhookApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization(ApplicationPermissions.ManageIntegrations).WithTags("Webhook notifications");
        admin.MapGet("/webhook-subscriptions", (WebhookService service, int page = 1, int pageSize = 20, Guid? applicationId = null) => service.Subscriptions(page, pageSize, applicationId));
        admin.MapPost("/webhook-subscriptions", async (CreateWebhookSubscription request, HttpContext context, WebhookService service) =>
        {
            var result = await service.Create(request, ActorOf(context)); return TypedResults.Created($"/api/admin/webhook-subscriptions/{result.Subscription.Id}", result);
        });
        admin.MapPatch("/webhook-subscriptions/{id:guid}", (Guid id, UpdateWebhookSubscription request, HttpContext context, WebhookService service) => service.Update(id, request, ActorOf(context)));
        admin.MapPost("/webhook-subscriptions/{id:guid}/rotate-secret", (Guid id, HttpContext context, WebhookService service) => service.Rotate(id, ActorOf(context)));
        admin.MapGet("/webhook-deliveries", (WebhookService service, int page = 1, int pageSize = 20, Guid? subscriptionId = null, string? status = null, Guid? applicationId = null) => service.Deliveries(page, pageSize, subscriptionId, status, applicationId));
        admin.MapPost("/webhook-deliveries/{id:guid}/retry", (Guid id, HttpContext context, WebhookService service) => service.Retry(id, ActorOf(context)));
    }
}
