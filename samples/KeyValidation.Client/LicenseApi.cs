using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyValidation.Client;

public sealed record LeaseGrant(DateTimeOffset ServerTime, DateTimeOffset ExpiresAt,
    DateTimeOffset LeaseExpiresAt, DateTimeOffset AbsoluteExpiresAt,
    int HeartbeatIntervalSeconds, string? SessionToken, Guid? LicenseId = null,
    bool IsTrial = false, string[]? Features = null, ClientVersionInfo? Version = null);

public sealed record ClientVersionInfo(string? ClientVersion, string? MinimumVersion, string? LatestVersion, bool EnforceMinimum,
    bool UpgradeAvailable, string? UpdateUrl, string? ReleaseNotes, string? Sha256);

public sealed class LicenseApi(HttpClient http)
{
    private readonly string? clientVersion;
    public LicenseApi(HttpClient http, string clientVersion) : this(http)
    {
        if (clientVersion is null || !Regex.IsMatch(clientVersion, @"\A(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z", RegexOptions.CultureInvariant))
            throw new InvalidOperationException("客户端版本必须是每段 0–9999 的 major.minor.patch。");
        this.clientVersion = clientVersion;
    }
    public async Task<LeaseGrant> ActivateTrialAsync(string appCode, string deviceId,
        string deviceLabel, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/client/trials/activate",
            new { appCode, deviceId, deviceLabel, clientVersion }, cancellationToken);
        await CheckResponse(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<LeaseGrant>(cancellationToken)
            ?? throw new InvalidDataException("Missing authorization response.");
    }

    public async Task<LeaseGrant> ActivateAsync(string appCode, string licenseKey,
        string deviceId, string deviceLabel, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsJsonAsync("/api/client/licenses/activate",
            new { appCode, licenseKey, deviceId, deviceLabel, clientVersion }, cancellationToken);
        await CheckResponse(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<LeaseGrant>(cancellationToken)
            ?? throw new InvalidDataException("Missing authorization response.");
    }

    public async Task<LeaseGrant> HeartbeatAsync(string sessionToken, CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest("/api/client/sessions/heartbeat", sessionToken);
        if (clientVersion is not null) request.Headers.Add("X-Client-Version", clientVersion);
        using var response = await http.SendAsync(request, cancellationToken);
        await CheckResponse(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<LeaseGrant>(cancellationToken)
            ?? throw new InvalidDataException("Missing authorization response.");
    }

    public async Task LogoutAsync(string sessionToken, CancellationToken cancellationToken)
    {
        using var request = AuthorizedRequest("/api/client/sessions/logout", sessionToken);
        using var response = await http.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage AuthorizedRequest(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task CheckResponse(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        if ((int)response.StatusCode >= 500 || (int)response.StatusCode == 429)
            throw new HttpRequestException("Authorization service temporarily unavailable.");
        var code = "AUTHORIZATION_REJECTED";
        ClientVersionInfo? version = null;
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.String
                && Regex.IsMatch(value.GetString()!, "^[A-Z_]{1,64}$")) code = value.GetString()!;
            if (json.RootElement.TryGetProperty("version", out var policy) && policy.ValueKind == JsonValueKind.Object)
                version = policy.Deserialize<ClientVersionInfo>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException) { }
        throw new LicenseRejectedException(code, version);
    }
}

public sealed class LicenseRejectedException(string code, ClientVersionInfo? version = null) : Exception(code)
{
    public ClientVersionInfo? Version { get; } = version;
}
