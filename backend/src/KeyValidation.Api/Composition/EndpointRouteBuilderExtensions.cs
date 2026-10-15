using KeyValidation.Api.DeveloperIntegration;
using KeyValidation.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace KeyValidation.Api;
public static class EndpointRouteBuilderExtensions
{
    public static WebApplication MapKeyValidation(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapGet("/health/live", () => TypedResults.Ok(new { status = "healthy" })).ExcludeFromDescription();
        app.MapGet("/health/ready", async (AppDbContext db) =>
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync("SELECT 1");
                return Results.Ok(new { status = "ready" });
            }
            catch
            {
                return Results.Problem(statusCode: 503, title: "DATABASE_UNAVAILABLE");
            }
        }).ExcludeFromDescription();
        app.MapControllers().Add(builder =>
        {
            // Keep route metadata used by documentation references and request metrics stable.
            if (builder is RouteEndpointBuilder endpoint && endpoint.RoutePattern.RawText is { } template)
                endpoint.RoutePattern = Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse("/" + template.TrimStart('/'));
        });
        app.MapDeveloperMcp();
        return app;
    }
}
