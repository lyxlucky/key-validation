namespace KeyValidation.Api.DeveloperIntegration;

public static class DeveloperDocumentationEndpoints
{
    public static void MapDeveloperDocumentationApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/integrations/developer-docs", (DeveloperDocumentationCatalog catalog) => TypedResults.Ok(catalog.List()))
            .WithTags("Developer documentation").AllowAnonymous().RequireRateLimiting("developer-docs");
        app.MapGet("/api/integrations/developer-docs/{id}", (string id, DeveloperDocumentationCatalog catalog) =>
            catalog.Get(id) is { } document ? Results.Ok(document) : Results.NotFound())
            .WithTags("Developer documentation").AllowAnonymous().RequireRateLimiting("developer-docs");
    }
}
