// .NET 10: create a console project, replace Program.cs with this file, dotnet run -- [--once].
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

await OnlineLicense.Run(args);

public sealed class Lease
{
    private readonly double seconds;
    private readonly long monotonicStart;
    private readonly DateTimeOffset wallStart;
    private readonly HashSet<string> features;
    public double Interval { get; }

    public Lease(JsonElement data, double roundTrip)
    {
        var server = data.GetProperty("serverTime").GetDateTimeOffset();
        seconds = new[] { "expiresAt", "leaseExpiresAt", "absoluteExpiresAt" }
            .Min(name => (data.GetProperty(name).GetDateTimeOffset() - server).TotalSeconds) - roundTrip;
        monotonicStart = Stopwatch.GetTimestamp();
        wallStart = DateTimeOffset.UtcNow;
        features = data.TryGetProperty("features", out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Select(value => value.GetString()!).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        Interval = Math.Max(0.01, data.GetProperty("heartbeatIntervalSeconds").GetDouble());
    }

    public double Remaining()
    {
        // UTC elapsed catches suspension; wall rollback cannot extend Stopwatch's budget.
        var elapsed = Math.Max(Stopwatch.GetElapsedTime(monotonicStart).TotalSeconds,
            (DateTimeOffset.UtcNow - wallStart).TotalSeconds);
        return Math.Max(0, seconds - Math.Max(0, elapsed));
    }

    public bool Allows(string feature) => Remaining() > 0 && features.Contains(feature);
}

public static class OnlineLicense
{
    private static string Setting(string name) => Environment.GetEnvironmentVariable("KEYVALIDATION_" + name)
        ?? throw new InvalidOperationException("Missing configuration");

    private static string DeviceId()
    {
        var configured = Environment.GetEnvironmentVariable("KEYVALIDATION_DEVICE_ID");
        if (!string.IsNullOrEmpty(configured)) return configured;
        var home = Environment.GetEnvironmentVariable(OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directory = Path.Combine(home, ".keyvalidation");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "device-id");
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream);
            writer.Write(Guid.NewGuid().ToString());
        }
        catch (IOException) when (File.Exists(path)) { }
        var value = File.ReadAllText(path).Trim();
        if (value.Length == 0) throw new InvalidOperationException("Device identifier file is empty");
        return value;
    }

    public static async Task Run(string[] args)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        string? token = null;
        var stopped = false;
        ConsoleCancelEventHandler stop = (_, e) => { e.Cancel = true; stopped = true; };
        Console.CancelKeyPress += stop;
        string? baseUrl = null;
        async Task<HttpResponseMessage> Request(string path, object? payload = null, double timeout = 10)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(0.001, timeout)));
            using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/api/client/" + path);
            if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var version = Environment.GetEnvironmentVariable("KEYVALIDATION_CLIENT_VERSION");
            if (path == "sessions/heartbeat" && !string.IsNullOrEmpty(version)) request.Headers.Add("X-Client-Version", version);
            if (payload is not null) request.Content = JsonContent.Create(payload);
            // ResponseContentRead includes the body in the cancellation budget.
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellation.Token);
        }
        try
        {
            baseUrl = Setting("API_URL").TrimEnd('/');
            var url = new Uri(baseUrl, UriKind.Absolute);
            var loopback = url.Host == "localhost" || (IPAddress.TryParse(url.Host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));
            if ((url.Scheme != "https" && !(url.Scheme == "http" && loopback)) || url.UserInfo.Length > 0
                || url.AbsolutePath != "/" || url.Query.Length > 0 || url.Fragment.Length > 0)
                throw new InvalidOperationException("API URL must be an HTTPS origin, or loopback HTTP for local development");
            var feature = Setting("REQUIRED_FEATURE");
            var started = Stopwatch.GetTimestamp();
            using var activation = await Request("licenses/activate", new
            {
                appCode = Setting("APP_CODE"), licenseKey = Setting("LICENSE_KEY"), deviceId = DeviceId(),
                clientVersion = Environment.GetEnvironmentVariable("KEYVALIDATION_CLIENT_VERSION")
            });
            if (!activation.IsSuccessStatusCode) return;
            var data = await activation.Content.ReadFromJsonAsync<JsonElement>();
            token = data.GetProperty("sessionToken").GetString();
            var lease = new Lease(data, Stopwatch.GetElapsedTime(started).TotalSeconds);
            var retry = 0.25;
            while (!stopped && lease.Allows(feature))
            {
                // Put business work behind this check, repeated for every operation and after resume.
                if (!lease.Allows(feature)) break;
                Console.WriteLine("Protected operation");
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(lease.Interval, lease.Remaining())));
                while (!stopped && lease.Allows(feature))
                {
                    var delay = retry;
                    started = Stopwatch.GetTimestamp();
                    try
                    {
                        using var heartbeat = await Request("sessions/heartbeat", timeout: Math.Min(10, lease.Remaining()));
                        if (heartbeat.IsSuccessStatusCode)
                        {
                            if (!lease.Allows(feature)) return;
                            data = await heartbeat.Content.ReadFromJsonAsync<JsonElement>();
                            lease = new Lease(data, Stopwatch.GetElapsedTime(started).TotalSeconds);
                            retry = 0.25;
                            break;
                        }
                        if (heartbeat.StatusCode != HttpStatusCode.TooManyRequests && (int)heartbeat.StatusCode < 500) return;
                        if (heartbeat.StatusCode == HttpStatusCode.TooManyRequests)
                        {
                            var after = heartbeat.Headers.RetryAfter;
                            delay = Math.Max(0, after?.Delta?.TotalSeconds ??
                                (after?.Date - DateTimeOffset.UtcNow)?.TotalSeconds ?? retry);
                        }
                    }
                    catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
                    { }
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, lease.Remaining())));
                    retry = Math.Min(2, retry * 2);
                }
                if (args.Contains("--once", StringComparer.Ordinal)) return;
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException or JsonException
            or InvalidOperationException or IOException or KeyNotFoundException or FormatException)
        {
            Console.Error.WriteLine("Authorization unavailable; protected operations stopped.");
        }
        finally
        {
            if (token is not null)
            {
                try { using var logout = await Request("sessions/logout", timeout: 2); }
                catch (Exception error) when (error is HttpRequestException or OperationCanceledException) { }
            }
            Console.CancelKeyPress -= stop;
        }
    }
}
