namespace KeyValidation.Api;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class CommerceEndpoints
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapCommerceApi(this WebApplication app) => app.MapControllers();
}
