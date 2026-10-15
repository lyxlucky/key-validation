namespace KeyValidation.Domain;
public sealed class ChannelPrice
{
    public Guid ChannelId { get; set; }
    public Guid ApplicationId { get; set; }
    public Guid PlanId { get; set; }
    public long UnitPriceCents { get; set; }
}
