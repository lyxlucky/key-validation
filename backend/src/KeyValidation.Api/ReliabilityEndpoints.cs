using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class ReliabilityEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    public static void MapReliabilityApi(this WebApplication app)
    {
        var admin = app.MapGroup("/api/admin").RequireAuthorization("Read").WithTags("Operations reliability");
        admin.MapGet("/catalog", (LicensingService service) => service.OperationsCatalog());
        admin.MapGet("/applications/{id:guid}/risk-policy", (Guid id, RiskService service) => service.Policy(id)).RequireAuthorization("Admin");
        admin.MapPut("/applications/{id:guid}/risk-policy", (Guid id, UpdateRiskPolicy request, HttpContext context, RiskService service) => service.SetPolicy(id, request, ActorOf(context))).RequireAuthorization("Admin");
        admin.MapGet("/risk-events", (RiskService service, int page = 1, int pageSize = 20, Guid? applicationId = null, string? status = null) => service.List(page, pageSize, applicationId, status)).RequireAuthorization("risk.review");
        admin.MapGet("/risk-events/{id:guid}", (Guid id, RiskService service) => service.Detail(id)).RequireAuthorization("risk.review");
        admin.MapPost("/risk-events/{id:guid}/review", (Guid id, ReviewRiskEvent request, HttpContext context, RiskService service) => service.Review(id, request, ActorOf(context))).RequireAuthorization("risk.review");
        admin.MapPost("/operations/scan", (OperationsScanner scanner, CancellationToken cancellationToken) => scanner.ScanAsync(cancellationToken)).RequireAuthorization("Admin");
        admin.MapGet("/applications/{id:guid}/notification-policy", (Guid id, NotificationService service) => service.Policy(id)).RequireAuthorization("Admin");
        admin.MapPut("/applications/{id:guid}/notification-policy", (Guid id, UpdateNotificationPolicy request, HttpContext context, NotificationService service) => service.SetPolicy(id, request, ActorOf(context))).RequireAuthorization("Admin");
        admin.MapGet("/notifications", (HttpContext context, NotificationService service, int page = 1, int pageSize = 20, bool unreadOnly = false, Guid? applicationId = null) => service.Inbox(ActorOf(context).Id, page, pageSize, unreadOnly, applicationId));
        admin.MapPost("/notifications/{id:guid}/read", async (Guid id, HttpContext context, NotificationService service) => { await service.MarkRead(id, ActorOf(context).Id); return TypedResults.NoContent(); });
    }
}
