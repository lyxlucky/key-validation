using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class OfflineSampleTests
{
    [Theory]
    [InlineData("expiry", "OFFLINE_LICENSE_EXPIRED", true)]
    [InlineData("disabled", "OFFLINE_DISABLED", false)]
    [InlineData("missing-file", "OFFLINE_FILE_UNAVAILABLE", false)]
    [InlineData("invalid-keys", "OFFLINE_KEY_CONFIGURATION_INVALID", false)]
    [InlineData("invalid-state", "OFFLINE_STATE_UNAVAILABLE", false)]
    public async Task ActualConsoleOfflineModeUsesPublicKeyFileAndNeverFallsBackToNetwork(string scenario, string expectedCode, bool initiallyGranted)
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyvalidation-offline-sample-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var file = Path.Combine(directory, "certificate.kglicense"); var keys = Path.Combine(directory, "trusted-keys.json");
        var state = Path.Combine(directory, "trusted-time.json"); var now = DateTimeOffset.UtcNow;
        static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "KG-OFFLINE", kid = "sample" }));
        var payload = Encode(JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 1, serial = Guid.NewGuid(), applicationCode = "sample", licenseId = Guid.NewGuid(), bindingId = Guid.NewGuid(),
            features = new[] { "basic" }, issuedAt = now.AddSeconds(-1), notBefore = now.AddSeconds(-1), expiresAt = now.AddSeconds(5), deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("sample\ndevice"))) }));
        var input = header + "." + payload;
        await File.WriteAllTextAsync(file, input + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
        await File.WriteAllTextAsync(keys, JsonSerializer.Serialize(new Dictionary<string, string> { ["sample"] = key.ExportSubjectPublicKeyInfoPem() }));
        if (scenario == "missing-file") File.Delete(file);
        if (scenario == "invalid-keys") await File.WriteAllTextAsync(keys, "not-json");
        if (scenario == "invalid-state") await File.WriteAllTextAsync(state, "not-json");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var calls = 0;
        using var stop = new CancellationTokenSource();
        var server = Task.Run(async () =>
        {
            try
            {
                while (!stop.IsCancellationRequested)
                {
                    using var connection = await listener.AcceptTcpClientAsync(stop.Token); Interlocked.Increment(ref calls);
                    await connection.GetStream().WriteAsync("HTTP/1.1 403 Forbidden\r\nContent-Length: 2\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{}"u8.ToArray(), stop.Token);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        });
        try
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(typeof(LicenseApi).Assembly.Location);
            start.Environment["KEYVALIDATION_OFFLINE_FILE"] = file; start.Environment["KEYVALIDATION_OFFLINE_KEYS_FILE"] = keys; start.Environment["KEYVALIDATION_OFFLINE_STATE_FILE"] = state;
            start.Environment["KEYVALIDATION_OFFLINE_ENABLED"] = scenario == "disabled" ? "false" : "true";
            start.Environment["KEYVALIDATION_APP_CODE"] = "sample"; start.Environment["KEYVALIDATION_DEVICE_ID"] = "device"; start.Environment["KEYVALIDATION_REQUIRED_FEATURE"] = "basic";
            start.Environment["KEYVALIDATION_API_URL"] = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}"; start.Environment["KEYVALIDATION_LICENSE_KEY"] = "unused-online-value";
            using var process = Process.Start(start)!; var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            Assert.Equal(0, Volatile.Read(ref calls)); Assert.Equal(2, process.ExitCode);
            Assert.Equal(initiallyGranted, (await output).Contains("离线授权已验证", StringComparison.Ordinal)); Assert.True((await error).Contains(expectedCode, StringComparison.Ordinal));
            if (initiallyGranted) Assert.True(File.Exists(state));
        }
        finally
        {
            stop.Cancel(); await server; listener.Stop();
            foreach (var entry in Directory.EnumerateFiles(directory)) File.Delete(entry); Directory.Delete(directory);
        }
    }
}
