namespace KeyValidation.Domain;
public sealed class StaffNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string RecipientId { get; set; } = "";
    public string EventType { get; set; } = "";
    public Guid ResourceId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}
