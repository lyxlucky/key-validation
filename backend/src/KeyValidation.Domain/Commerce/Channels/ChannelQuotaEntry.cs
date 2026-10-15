namespace KeyValidation.Domain;
public sealed class ChannelQuotaEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ChannelId { get; set; }
    public string Kind { get; set; } = "";
    public long Delta { get; set; }
    public long BalanceAfter { get; set; }
    public string Reason { get; set; } = "";
    public string ActorId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? OrderId { get; set; }
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
}
