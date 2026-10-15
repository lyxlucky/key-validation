using System.Text.Json;

namespace KeyValidation.Desktop;

internal static class LocalPreferences
{
    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyValidation.Desktop");
    public static string LoadDeviceId(string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device.json");
        if (File.Exists(path))
        {
            using var input = File.OpenRead(path);
            if (input.Length is < 2 or > 2048) throw new ProfileException("DEVICE_ID_INVALID");
            using var json = JsonDocument.Parse(input);
            if (!json.RootElement.TryGetProperty("deviceId", out var value) || value.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 256) throw new ProfileException("DEVICE_ID_INVALID");
            return value.GetString()!;
        }
        var device = "desktop-" + Guid.NewGuid().ToString("N");
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(output, new { schemaVersion = 1, deviceId = device });
        output.Flush(flushToDisk: true);
        return device;
    }
}
