namespace KeyValidation.Api.DeveloperIntegration;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class DeveloperDocumentationEndpoints
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapDeveloperDocumentationApi(this WebApplication app) => app.MapControllers();
}
