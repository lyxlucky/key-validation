using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyValidation.Client;
using Xunit;

namespace KeyValidation.Client.Tests;

public sealed class OfflineLicenseTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid LicenseId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BindingId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static Dictionary<string, object?> Payload(DateTimeOffset? issued = null, DateTimeOffset? notBefore = null, DateTimeOffset? expires = null) => new()
    {
        ["formatVersion"] = 1, ["serial"] = Guid.Parse("33333333-3333-3333-3333-333333333333"), ["applicationCode"] = "product",
        ["licenseId"] = LicenseId, ["bindingId"] = BindingId, ["features"] = new[] { "basic", "export.pdf" },
        ["issuedAt"] = issued ?? Now.AddHours(-1), ["notBefore"] = notBefore ?? Now.AddHours(-1), ["expiresAt"] = expires ?? Now.AddHours(1),
        ["deviceHash"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("product\n raw-device ")))
    };
    private static string Sign(ECDsa key, Dictionary<string, object?>? payload = null, string kid = "current", string alg = "ES256", string type = "KG-OFFLINE")
    {
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg, typ = type, kid }));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(payload ?? Payload())); var input = header + "." + body;
        return input + "." + Encode(key.SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static OfflineLicenseVerifier Verifier(ECDsa key, ITrustedTimeStore? store = null, TestClock? clock = null) => new(new Dictionary<string, string> { ["current"] = key.ExportSubjectPublicKeyInfoPem() }, store ?? new MemoryStore(), clock ?? new TestClock());
    private static void Error(string code, Action action) => Assert.Equal(code, Assert.Throws<OfflineLicenseException>(action).Code);

    [Fact]
    public void RealSignatureGrantsOnlyDeclaredFeaturesAndUsesNormalizedApplicationButRawDevice()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var state = new MemoryStore();
        var session = Verifier(key, state).Open(Sign(key), " PRODUCT ", " raw-device ", ["export.pdf"]);
        session.Demand("export.pdf"); session.Demand("basic"); Assert.Equal(Now, state.Latest);
        Error("FEATURE_NOT_GRANTED", () => session.Demand("admin"));
        Error("OFFLINE_DEVICE_MISMATCH", () => Verifier(key).Open(Sign(key), "product", "raw-device"));
    }

    [Fact]
    public void MutatingOnePayloadByteInvalidatesTheRealSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var parts = Sign(key).Split('.');
        var body = Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + new string('=', (4 - parts[1].Length % 4) % 4)); body[body.Length / 2] ^= 1;
        parts[1] = Encode(body); Error("OFFLINE_LICENSE_INVALID", () => Verifier(key).Open(string.Join('.', parts), "product", " raw-device "));
    }

    [Theory]
    [InlineData("unknown", "ES256", "KG-OFFLINE", "OFFLINE_KEY_UNKNOWN")]
    [InlineData("current", "RS256", "KG-OFFLINE", "OFFLINE_LICENSE_INVALID")]
    [InlineData("current", "none", "KG-OFFLINE", "OFFLINE_LICENSE_INVALID")]
    [InlineData("current", "ES256", "JWT", "OFFLINE_LICENSE_INVALID")]
    public void KeyAlgorithmAndTypeAreTakenOnlyFromThePinnedProtocol(string kid, string algorithm, string type, string code)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Error(code, () => Verifier(key).Open(Sign(key, kid: kid, alg: algorithm, type: type), "product", " raw-device "));
    }

    [Theory]
    [InlineData("wrong-app", " raw-device ", "OFFLINE_APPLICATION_MISMATCH")]
    [InlineData("product", "wrong-device", "OFFLINE_DEVICE_MISMATCH")]
    public void WrongIdentityNeverGrantsRights(string app, string device, string code)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); Error(code, () => Verifier(key).Open(Sign(key), app, device));
    }

    [Fact]
    public void ExactExpiryAndNotBeforeAreEnforced()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Error("OFFLINE_LICENSE_EXPIRED", () => Verifier(key).Open(Sign(key, Payload(expires: Now)), "product", " raw-device "));
        Error("OFFLINE_LICENSE_NOT_YET_VALID", () => Verifier(key).Open(Sign(key, Payload(notBefore: Now.AddMinutes(1))), "product", " raw-device "));
    }

    [Fact]
    public void UnsupportedFormatAndInvalidEntitlementPayloadsAreRejected()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var format = Payload(); format["formatVersion"] = 2;
        Error("OFFLINE_LICENSE_INVALID", () => Verifier(key).Open(Sign(key, format), "product", " raw-device "));
        var feature = Payload(); feature["features"] = new[] { "basic", "Basic" };
        Error("OFFLINE_LICENSE_INVALID", () => Verifier(key).Open(Sign(key, feature), "product", " raw-device "));
        var identity = Payload(); identity["bindingId"] = Guid.Empty;
        Error("OFFLINE_LICENSE_INVALID", () => Verifier(key).Open(Sign(key, identity), "product", " raw-device "));
        var extra = Payload(); extra["publicKey"] = key.ExportSubjectPublicKeyInfoPem();
        Error("OFFLINE_LICENSE_INVALID", () => Verifier(key).Open(Sign(key, extra), "product", " raw-device "));
    }

    [Fact]
    public void MonotonicElapsedTimeExpiresRightsEvenIfWallClockDoesNotAdvance()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var clock = new TestClock();
        var session = Verifier(key, clock: clock).Open(Sign(key, Payload(expires: Now.AddMinutes(2))), "product", " raw-device ");
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(1)); Assert.Equal(TimeSpan.FromMinutes(1), session.Remaining);
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(1)); Error("OFFLINE_LICENSE_EXPIRED", () => session.Demand("basic"));
    }

    [Fact]
    public void TrustedTimePersistsAcrossRestartsAndRejectsRollbackBeyondFiveMinutes()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var state = new MemoryStore();
        var first = Verifier(key, state).Open(Sign(key), "product", " raw-device "); first.Demand("basic");
        Error("OFFLINE_CLOCK_ROLLBACK", () => Verifier(key, state, new TestClock { WallTime = Now.AddMinutes(-5).AddTicks(-1) }).Open(Sign(key), "product", " raw-device "));
        var permitted = Verifier(key, state, new TestClock { WallTime = Now.AddMinutes(-5) }).Open(Sign(key), "product", " raw-device ");
        Assert.Equal(TimeSpan.FromHours(1), permitted.Remaining); Assert.Equal(Now, state.Latest);
    }

    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public void InaccessibleOrFailedPersistenceNeverGrantsOfflineAbility(bool readFails, bool saveFails)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var state = new MemoryStore { ReadFails = readFails, SaveFails = saveFails };
        Error("OFFLINE_STATE_UNAVAILABLE", () => Verifier(key, state).Open(Sign(key), "product", " raw-device "));
    }

    [Fact]
    public void PersistenceFailureDuringAProtectedOperationStopsTheGrant()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var state = new MemoryStore();
        var session = Verifier(key, state).Open(Sign(key), "product", " raw-device "); state.SaveFails = true;
        Error("OFFLINE_STATE_UNAVAILABLE", () => session.Demand("basic")); state.SaveFails = false;
        Error("OFFLINE_STATE_UNAVAILABLE", () => session.Demand("basic"));
    }

    [Fact]
    public void ObservedExpiryIsPersistedSoARestartWithinClockToleranceCannotReviveIt()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256); var state = new MemoryStore(); var clock = new TestClock();
        var content = Sign(key, Payload(expires: Now.AddMinutes(2)));
        var session = Verifier(key, state, clock).Open(content, "product", " raw-device ");
        clock.AdvanceMonotonic(TimeSpan.FromMinutes(2)); clock.WallTime = Now.AddMinutes(2);
        Error("OFFLINE_LICENSE_EXPIRED", () => session.Demand("basic"));
        Error("OFFLINE_LICENSE_EXPIRED", () => Verifier(key, state, new TestClock { WallTime = Now.AddMinutes(1) }).Open(content, "product", " raw-device "));
    }

    [Fact]
    public void RequiredFeatureMustBePresentBeforeReturningAGrant()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Error("FEATURE_NOT_GRANTED", () => Verifier(key).Open(Sign(key), "product", " raw-device ", ["enterprise"]));
    }

    [Fact]
    public void PinnedOldAndNewPublicKeysSupportRotationWithoutPayloadKeyTrust()
    {
        using var first = ECDsa.Create(ECCurve.NamedCurves.nistP256); using var next = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var trusted = new Dictionary<string, string> { ["current"] = first.ExportSubjectPublicKeyInfoPem(), ["next"] = next.ExportSubjectPublicKeyInfoPem() };
        var verifier = new OfflineLicenseVerifier(trusted, new MemoryStore(), new TestClock());
        verifier.Open(Sign(first), "product", " raw-device ").Demand("basic");
        verifier.Open(Sign(next, kid: "next"), "product", " raw-device ").Demand("export.pdf");
        Error("OFFLINE_LICENSE_INVALID", () => verifier.Open(Sign(next), "product", " raw-device "));
    }

    [Fact]
    public void FileStoreRetainsTheLatestUtcAndCorruptionDoesNotResetTrust()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyvalidation-offline-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "trusted-time.json");
        try
        {
            var store = new FileTrustedTimeStore(path); Assert.Null(store.ReadLatest()); Assert.Equal(Now, store.SaveLatest(Now));
            Assert.Equal(Now, new FileTrustedTimeStore(path).SaveLatest(Now.AddMinutes(-1)));
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Error("OFFLINE_CLOCK_ROLLBACK", () => Verifier(key, new FileTrustedTimeStore(path), new TestClock { WallTime = Now.AddMinutes(-6) }).Open(Sign(key), "product", " raw-device "));
            File.WriteAllText(path, "malformed");
            Error("OFFLINE_STATE_UNAVAILABLE", () => Verifier(key, new FileTrustedTimeStore(path)).Open(Sign(key), "product", " raw-device "));
        }
        finally { foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file); Directory.Delete(directory); }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset WallTime { get; set; } = Now;
        private long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow() => WallTime;
        public override long GetTimestamp() => ticks;
        public void AdvanceMonotonic(TimeSpan duration) => ticks += duration.Ticks;
    }
    private sealed class MemoryStore : ITrustedTimeStore
    {
        public DateTimeOffset? Latest { get; private set; }
        public bool ReadFails { get; set; }
        public bool SaveFails { get; set; }
        public DateTimeOffset? ReadLatest() => ReadFails ? throw new IOException() : Latest;
        public DateTimeOffset SaveLatest(DateTimeOffset value)
        {
            if (SaveFails) throw new IOException(); if (Latest is null || value > Latest) Latest = value; return Latest.Value;
        }
    }
}
