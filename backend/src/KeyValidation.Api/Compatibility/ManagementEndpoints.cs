namespace KeyValidation.Api;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class ManagementEndpoints
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapManagementApi(this WebApplication app) => app.MapControllers();
}
