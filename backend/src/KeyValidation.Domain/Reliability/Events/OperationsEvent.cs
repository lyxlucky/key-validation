namespace KeyValidation.Domain;
public sealed class OperationsEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public Guid ResourceId { get; set; }
    public string EventType { get; set; } = "";
    public string DeduplicationKey { get; set; } = "";
    public string BodyJson { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
}
