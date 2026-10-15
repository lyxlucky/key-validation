namespace KeyValidation.Application;

public static class ApplicationCode
{
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? "";
}
