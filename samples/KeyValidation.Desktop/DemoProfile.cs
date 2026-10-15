using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyValidation.Desktop;

public enum AuthorizationMode { Online, Offline }

public sealed class DemoProfile
{
    public int SchemaVersion { get; init; } = 1;
    public AuthorizationMode Mode { get; init; }
    public string ApiUrl { get; init; } = "http://127.0.0.1:5080";
    public string AppCode { get; init; } = "demo-app";
    public string ClientVersion { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string DeviceLabel { get; init; } = "Windows 测试设备";
    public string? LicenseKey { get; init; }
    public string? OfflineLicensePath { get; init; }
    public string? TrustedKeysPath { get; init; }
    public void Validate()
    {
        if (SchemaVersion != 1 || !Enum.IsDefined(Mode)) throw new ProfileException("PROFILE_INVALID");
        ValidateApiUrl(ApiUrl);
        if (AppCode is null || !Regex.IsMatch(AppCode.Trim().ToLowerInvariant(), @"\A[a-z0-9_-]{3,64}\z", RegexOptions.CultureInvariant))
            throw new ProfileException("APP_CODE_INVALID");
        if (ClientVersion is null || ClientVersion.Length != 0 && !Regex.IsMatch(ClientVersion, @"\A(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\.(0|[1-9][0-9]{0,3})\z", RegexOptions.CultureInvariant))
            throw new ProfileException("CLIENT_VERSION_INVALID");
        if (string.IsNullOrWhiteSpace(DeviceId) || DeviceId.Length > 256 || DeviceId.Any(char.IsControl)) throw new ProfileException("DEVICE_ID_INVALID");
        if (DeviceLabel is null || DeviceLabel.Length > 120 || DeviceLabel.Any(char.IsControl)) throw new ProfileException("DEVICE_LABEL_INVALID");
        if (LicenseKey?.Length > 1024) throw new ProfileException("PROFILE_INVALID");
    }

    public static Uri ValidateApiUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 || value.Any(char.IsControl) ||
            value.IndexOfAny(['@', '?', '#', '\\']) >= 0 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" ||
            !(uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new ProfileException("API_URL_INVALID");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>Only an explicit file selection/CLI path reads a card. Loading never activates or writes it back.</summary>
    public static async Task<DemoProfile> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            if (stream.Length is < 2 or > 65536) throw new ProfileException("PROFILE_INVALID");
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new ProfileException("PROFILE_INVALID");
            var names = root.EnumerateObject().Select(x => x.Name).ToArray();
            string[] allowed = ["schemaVersion", "mode", "apiUrl", "appCode", "clientVersion", "deviceId", "deviceLabel", "licenseKey", "offlineLicensePath", "trustedKeysPath"];
            if (names.Distinct(StringComparer.Ordinal).Count() != names.Length || names.Except(allowed, StringComparer.Ordinal).Any() ||
                !root.TryGetProperty("schemaVersion", out var schema) || !schema.TryGetInt32(out var schemaVersion) || schemaVersion != 1)
                throw new ProfileException("PROFILE_INVALID");
            string? Read(string name, bool required = false)
            {
                if (!root.TryGetProperty(name, out var item) || item.ValueKind == JsonValueKind.Null)
                {
                    if (required) throw new ProfileException("PROFILE_INVALID");
                    return null;
                }
                if (item.ValueKind != JsonValueKind.String) throw new ProfileException("PROFILE_INVALID");
                return item.GetString();
            }
            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            string? Resolve(string? file) => string.IsNullOrWhiteSpace(file) ? null : Path.GetFullPath(file, directory);
            var profile = new DemoProfile
            {
                SchemaVersion = schemaVersion,
                Mode = Read("mode", true) switch { "online" => AuthorizationMode.Online, "offline" => AuthorizationMode.Offline, _ => throw new ProfileException("PROFILE_INVALID") },
                ApiUrl = Read("apiUrl", true)!, AppCode = Read("appCode", true)!, ClientVersion = Read("clientVersion") ?? "",
                DeviceId = Read("deviceId", true)!, DeviceLabel = Read("deviceLabel") ?? "Windows 测试设备",
                LicenseKey = Read("licenseKey"), OfflineLicensePath = Resolve(Read("offlineLicensePath")), TrustedKeysPath = Resolve(Read("trustedKeysPath"))
            };
            profile.Validate();
            return profile;
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { throw new ProfileException("PROFILE_INVALID"); }
    }
}

public sealed class ProfileException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
