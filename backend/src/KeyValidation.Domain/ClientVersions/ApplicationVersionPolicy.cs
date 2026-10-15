namespace KeyValidation.Domain;
public sealed class ApplicationVersionPolicy
{
    public Guid ApplicationId { get; set; }
    public string? MinimumVersion { get; set; }
    public string? LatestVersion { get; set; }
    public bool EnforceMinimum { get; set; }
    public string? UpdateUrl { get; set; }
    public string? ReleaseNotes { get; set; }
    public string? Sha256 { get; set; }
}
