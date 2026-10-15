using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using KeyValidation.Client;

namespace KeyValidation.Desktop;

internal static class DesktopProtocol
{
    public static async Task CheckedCommandAsync(HttpClient http, string path, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        await CheckResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.NoContent) throw new InvalidDataException();
    }

    public static async Task<ClientVersionInfo> CheckVersionAsync(HttpClient http, DemoProfile profile, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync("/api/client/applications/" + Uri.EscapeDataString(profile.AppCode.Trim().ToLowerInvariant()) + "/version", cancellationToken).ConfigureAwait(false);
        await CheckResponseAsync(response, cancellationToken).ConfigureAwait(false);
        var policy = await response.Content.ReadFromJsonAsync<PublicVersion>(cancellationToken).ConfigureAwait(false) ?? throw new InvalidDataException();
        if (!string.Equals(policy.ApplicationCode, profile.AppCode.Trim(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
        var current = string.IsNullOrEmpty(profile.ClientVersion) ? null : profile.ClientVersion;
        var newer = policy.LatestVersion is { } latest && current is { } supplied && Version.TryParse(latest, out var target) &&
            Version.TryParse(supplied, out var installed) && target > installed;
        return new(current, policy.MinimumVersion, policy.LatestVersion, policy.EnforceMinimum, newer,
            policy.UpdateUrl, policy.ReleaseNotes, policy.Sha256);
    }

    private static async Task CheckResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429) throw new HttpRequestException();
        var code = "AUTHORIZATION_REJECTED";
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false), cancellationToken: cancellationToken).ConfigureAwait(false);
            if (json.RootElement.TryGetProperty("code", out var item) && item.ValueKind == JsonValueKind.String)
                code = ErrorCatalog.SafeCode(item.GetString());
        }
        catch (JsonException) { }
        throw new LicenseRejectedException(code);
    }

    private sealed record PublicVersion(string ApplicationCode, string? MinimumVersion, string? LatestVersion,
        bool EnforceMinimum, string? UpdateUrl, string? ReleaseNotes, string? Sha256);
}
