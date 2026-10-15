namespace KeyValidation.Domain;
public sealed class AdminAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public Guid TargetId { get; set; }
    public Guid? ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Category { get; set; } = "";
    public string Result { get; set; } = "";
    public string? ActorName { get; set; }
    public string? TargetType { get; set; }
    public string? TargetName { get; set; }
    public string? ApplicationName { get; set; }
    public string? SourceIp { get; set; }
    public string? Method { get; set; }
    public string? ErrorCategory { get; set; }
    public string Changes { get; set; } = "{}";
    public DateTimeOffset CreatedAt { get; set; }
    public string TraceId { get; set; } = "";
}
