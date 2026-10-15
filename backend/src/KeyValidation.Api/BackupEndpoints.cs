using System.Security.Claims;
using KeyValidation.Application;
using KeyValidation.Domain;
using KeyValidation.Infrastructure;

namespace KeyValidation.Api;

public static class BackupEndpoints
{
    private static Actor ActorOf(HttpContext context) => new(context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new RuleException("UNAUTHENTICATED", 401), context.TraceIdentifier);
    public static void MapBackupApi(this WebApplication app)
    {
        var backups = app.MapGroup("/api/admin/backups").RequireAuthorization("Admin").WithTags("Encrypted backups");
        backups.MapGet("/status", (BackupService service, CancellationToken cancellationToken) => service.Status(cancellationToken));
        backups.MapGet("", (BackupService service, CancellationToken cancellationToken, int page = 1, int pageSize = 20) => service.List(page, pageSize, cancellationToken));
        backups.MapPost("", async (BackupService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(ActorOf(context), cancellationToken); return TypedResults.Created($"/api/admin/backups/{result.Id}", result);
        });
        backups.MapPost("/{id:guid}/verify", (Guid id, BackupService service, HttpContext context, CancellationToken cancellationToken) => service.VerifyAsync(id, ActorOf(context), cancellationToken));
    }
}
