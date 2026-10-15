using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyValidation.Desktop;

public sealed class ClientSettings
{
    public string ApplicationName { get; init; } = "KeyGate 授权客户端";
    public string ApiUrl { get; init; } = "http://127.0.0.1:5080";
    public string AppCode { get; init; } = "keygate-desktop-client";
    public string ClientVersion { get; init; } = "1.0.0";

    public DemoProfile CreateProfile(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(ApplicationName) || ApplicationName.Length > 80 || ApplicationName.Any(char.IsControl))
            throw new ProfileException("PROFILE_INVALID");
        var profile = new DemoProfile { ApiUrl = ApiUrl, AppCode = AppCode, ClientVersion = ClientVersion, DeviceId = deviceId, DeviceLabel = Environment.MachineName };
        profile.Validate();
        return profile;
    }

    public static ClientSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length is < 2 or > 16384) throw new ProfileException("PROFILE_INVALID");
            return JsonSerializer.Deserialize<ClientSettings>(stream, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
            }) ?? throw new ProfileException("PROFILE_INVALID");
        }
        catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException)
        { throw new ProfileException("PROFILE_INVALID"); }
    }
}
