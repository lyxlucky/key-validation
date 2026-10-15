namespace KeyValidation.Domain;
public sealed class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string AccountId { get; set; } = "";
    public Guid[] ApplicationIds { get; set; } = [];
    public long RemainingQuota { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
