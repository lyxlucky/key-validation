namespace KeyValidation.Client;

/// <summary>Checks the latest server-granted feature snapshot against the live lease.</summary>
public sealed class LicenseCapabilities(LeaseTracker lease)
{
    private HashSet<string> features = new(StringComparer.Ordinal);

    public void Update(IEnumerable<string>? grantedFeatures) =>
        features = grantedFeatures?.ToHashSet(StringComparer.Ordinal) ?? new(StringComparer.Ordinal);

    public bool Allows(string feature) => lease.IsValid && features.Contains(feature);
}
