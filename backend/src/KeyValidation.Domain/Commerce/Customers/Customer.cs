namespace KeyValidation.Domain;
public sealed class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DisabledAt { get; set; }
    public DateTimeOffset? AnonymizedAt { get; set; }
    public string? AccessCodeDigest { get; set; }
    public Guid AccessVersion { get; set; } = Guid.NewGuid();
}
