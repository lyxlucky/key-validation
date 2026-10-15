using System.Text.Json;

namespace KeyValidation.Client;

public sealed class FileTrustedTimeStore : ITrustedTimeStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private readonly string path;
    public FileTrustedTimeStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A persistent trusted-time path is required.", nameof(path));
        this.path = Path.GetFullPath(path);
    }
    public DateTimeOffset? ReadLatest() { using var held = Acquire(); return Read(); }
    public DateTimeOffset SaveLatest(DateTimeOffset value)
    {
        using var held = Acquire(); var previous = Read(); var latest = previous > value ? previous.Value : value.ToUniversalTime();
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                JsonSerializer.Serialize(stream, new State(1, latest), Json); stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true); return latest;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private FileStream Acquire()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }
    private DateTimeOffset? Read()
    {
        if (!File.Exists(path)) return null;
        using var stream = File.OpenRead(path); if (stream.Length is < 1 or > 1024) throw new InvalidDataException("Invalid offline time state.");
        using var json = JsonDocument.Parse(stream);
        if (json.RootElement.ValueKind != JsonValueKind.Object || json.RootElement.EnumerateObject().Count() != 2 ||
            !json.RootElement.TryGetProperty("formatVersion", out _) || !json.RootElement.TryGetProperty("latestUtc", out _)) throw new InvalidDataException("Invalid offline time state.");
        var state = json.Deserialize<State>(Json);
        if (state is null || state.FormatVersion != 1 || state.LatestUtc == default) throw new InvalidDataException("Invalid offline time state.");
        return state.LatestUtc.ToUniversalTime();
    }
    private sealed record State(int FormatVersion, DateTimeOffset LatestUtc);
}
