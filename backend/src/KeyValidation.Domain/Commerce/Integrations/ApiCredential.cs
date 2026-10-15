namespace KeyValidation.Domain;
public sealed class ApiCredential
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid? ChannelId { get; set; }
    public string Name { get; set; } = "";
    public string[] Permissions { get; set; } = [];
    public string SecretDigest { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
