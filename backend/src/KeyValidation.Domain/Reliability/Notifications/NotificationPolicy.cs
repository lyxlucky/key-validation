namespace KeyValidation.Domain;
public sealed class NotificationPolicy
{
    public Guid ApplicationId { get; set; }
    public bool Enabled { get; set; }
    public string[] RecipientIds { get; set; } = [];
    public string[] EventTypes { get; set; } = [];
    public int[] ExpiryDays { get; set; } = [3, 7, 30];
}
