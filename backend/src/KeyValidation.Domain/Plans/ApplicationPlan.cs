namespace KeyValidation.Domain;
public sealed class ApplicationPlan
{
    public Guid ApplicationId { get; set; }
    public Guid PlanId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
