namespace KeyValidation.Api;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class AdminAccounts
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapAccountApi(this WebApplication app) => app.MapControllers();
}
