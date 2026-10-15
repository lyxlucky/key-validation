using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Desktop.Tests;

public sealed class OfflineCoordinatorTests
{
    [Fact]
    public async Task ExplicitSignedFileAndIndependentKeyImportGrantBasicAndFeatureWithoutHttp()
    {
        using var fixture = new OfflineFixture();
        var result = await fixture.Validate();
        Assert.True(result.Success);
        Assert.True(fixture.Online.Coordinator.PerformBasicAction().Success);
        var export = Path.Combine(fixture.Directory.Path, "offline.csv");
        Assert.True(fixture.Online.Coordinator.ExportCsv(export).Success);
        Assert.Contains("演示项目", File.ReadAllText(export));
        Assert.Equal(0, fixture.Online.ClientCreations);
        Assert.Equal(0, fixture.Online.Handler.Calls);
    }

    [Theory]
    [InlineData("wrong-app", "offline-device", "OFFLINE_APPLICATION_MISMATCH")]
    [InlineData("desktop-test", "wrong-device", "OFFLINE_DEVICE_MISMATCH")]
    public async Task SignedOfflineIdentityMismatchNeverFallsBackToHttp(string app, string device, string code)
    {
        using var fixture = new OfflineFixture();
        fixture.Online.Coordinator.Configure(new DemoProfile { Mode = AuthorizationMode.Offline, AppCode = app, DeviceId = device });
        Assert.Equal(code, (await fixture.Validate()).Code);
        Assert.False(fixture.Online.Coordinator.PerformBasicAction().Success);
        await fixture.Online.Coordinator.ActivateAsync("ignored-card");
        await fixture.Online.Coordinator.HeartbeatAsync();
        await fixture.Online.Coordinator.TickAsync();
        Assert.Equal(0, fixture.Online.ClientCreations);
        Assert.Equal(0, fixture.Online.Handler.Calls);
    }

    [Fact]
    public async Task TamperedSignedFileIsRejectedWithoutNetwork()
    {
        using var fixture = new OfflineFixture();
        var parts = File.ReadAllText(fixture.LicensePath).Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        File.WriteAllText(fixture.LicensePath, string.Join('.', parts));
        Assert.Equal("OFFLINE_LICENSE_INVALID", (await fixture.Validate()).Code);
        Assert.False(fixture.Online.Coordinator.PerformBasicAction().Success);
        Assert.Equal(0, fixture.Online.Handler.Calls);
    }

    [Fact]
    public async Task OfflineExportRechecksTimeAfterTheSaveDialogAndDoesNotWriteAtExactExpiry()
    {
        using var fixture = new OfflineFixture();
        Assert.True((await fixture.Validate()).Success);
        fixture.Online.Clock.Advance(TimeSpan.FromHours(1));
        var output = Path.Combine(fixture.Directory.Path, "expired.csv");
        Assert.Equal("OFFLINE_LICENSE_EXPIRED", fixture.Online.Coordinator.ExportCsv(output).Code);
        Assert.False(File.Exists(output));
        Assert.False(fixture.Online.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task OfflineRollbackIsDetectedByEachProtectedOperationUsingOnlyAnIsolatedClock()
    {
        using var fixture = new OfflineFixture();
        Assert.True((await fixture.Validate()).Success);
        fixture.Online.Clock.Utc -= TimeSpan.FromMinutes(6);
        Assert.Equal("OFFLINE_CLOCK_ROLLBACK", fixture.Online.Coordinator.PerformBasicAction().Code);
        fixture.Online.Clock.Utc += TimeSpan.FromMinutes(6);
        Assert.False(fixture.Online.Coordinator.PerformBasicAction().Success);
    }

    [Fact]
    public async Task CorruptPersistentTimeIsNeverRepairedToGrantPermission()
    {
        using var fixture = new OfflineFixture();
        var path = Path.Combine(fixture.Directory.Path, "trusted-time.json");
        File.WriteAllText(path, "corrupt-state");
        using var coordinator = new SessionCoordinator(_ => throw new InvalidOperationException("No HTTP allowed"), new FileTrustedTimeStore(path), fixture.Online.Clock);
        coordinator.Configure(OfflineFixture.Profile);
        Assert.Equal("OFFLINE_STATE_UNAVAILABLE", (await coordinator.ValidateOfflineAsync(fixture.LicensePath, fixture.KeysPath)).Code);
        Assert.False(coordinator.PerformBasicAction().Success);
        Assert.Equal("corrupt-state", File.ReadAllText(path));
    }

    [Fact]
    public async Task StateFailureWhileRefreshingTheOfflineSnapshotFailsClosed()
    {
        using var fixture = new OfflineFixture();
        Assert.True((await fixture.Validate()).Success);
        fixture.Online.TimeStore.Unavailable = true;
        var snapshot = fixture.Online.Coordinator.Snapshot();
        Assert.False(snapshot.Authorized);
        Assert.False(snapshot.CanExport);
        Assert.False(fixture.Online.Coordinator.PerformBasicAction().Success);
    }
}

internal sealed class OfflineFixture : IDisposable
{
    public TemporaryDirectory Directory { get; } = new();
    public CoordinatorFixture Online { get; } = new();
    public string LicensePath => Path.Combine(Directory.Path, "signed.kglicense");
    public string KeysPath => Path.Combine(Directory.Path, "trusted-keys.json");
    public static DemoProfile Profile => new() { Mode = AuthorizationMode.Offline, AppCode = "desktop-test", DeviceId = "offline-device" };
    public OfflineFixture()
    {
        Online.Coordinator.Configure(Profile);
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        File.WriteAllText(KeysPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["test-key"] = key.ExportSubjectPublicKeyInfoPem() }));
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "KG-OFFLINE", kid = "test-key" }));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(new
        {
            formatVersion = 1, serial = Guid.NewGuid(), applicationCode = "desktop-test", licenseId = Guid.NewGuid(), bindingId = Guid.NewGuid(),
            features = new[] { "export" }, issuedAt = Online.Clock.Utc.AddMinutes(-1), notBefore = Online.Clock.Utc.AddMinutes(-1), expiresAt = Online.Clock.Utc.AddHours(1),
            deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("desktop-test\noffline-device")))
        }));
        var input = header + "." + body;
        File.WriteAllText(LicensePath, input + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
    }
    public Task<OperationResult> Validate() => Online.Coordinator.ValidateOfflineAsync(LicensePath, KeysPath);
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public void Dispose() { Online.Dispose(); Directory.Dispose(); }
}
