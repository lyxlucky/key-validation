namespace KeyValidation.Domain;
public sealed class CommerceReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ActorId { get; set; } = "";
    public string Operation { get; set; } = "";
    public Guid RequestId { get; set; }
    public string Fingerprint { get; set; } = "";
    public Guid ResourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
