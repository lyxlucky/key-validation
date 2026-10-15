namespace KeyValidation.Domain;
public sealed class WebhookSubscription
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string[] EventTypes { get; set; } = [];
    public bool IsEnabled { get; set; } = true;
    public string ProtectedSecret { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
