using System.Text.RegularExpressions;

namespace KeyValidation.Domain;
public static class AuthorizationPolicies
{
    public static void Validate(int sessions, int? activationDays)
    {
        if (sessions is < 1 or > 100 || activationDays is < 1 or > 3650)
            throw new RuleException("VALIDATION_ERROR", 400);
    }

    public static string[] NormalizeFeatures(string[]? features)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var feature in features ?? [])
        {
            var normalized = feature?.Trim().ToLowerInvariant();
            if (normalized is null || !Regex.IsMatch(normalized, "\\A[a-z][a-z0-9._-]{0,63}\\z"))
                throw new RuleException("VALIDATION_ERROR", 400);
            if (seen.Add(normalized))
                result.Add(normalized);
            if (result.Count > 50)
                throw new RuleException("VALIDATION_ERROR", 400);
        }

        return result.ToArray();
    }
}
