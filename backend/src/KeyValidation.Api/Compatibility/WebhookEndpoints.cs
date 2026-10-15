namespace KeyValidation.Api;

/// <summary>Compatibility entry point for hosts using the former route registration API.</summary>
public static class WebhookEndpoints
{
    [Obsolete("Register MVC services and use MapControllers().")]
    public static void MapWebhookApi(this WebApplication app) => app.MapControllers();
}
