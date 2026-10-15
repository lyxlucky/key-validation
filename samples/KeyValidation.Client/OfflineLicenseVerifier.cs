using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyValidation.Client;

public interface ITrustedTimeStore
{
    DateTimeOffset? ReadLatest();
    DateTimeOffset SaveLatest(DateTimeOffset value);
}

public sealed class OfflineLicenseException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed class OfflineLicenseVerifier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };
    private static readonly HashSet<string> PayloadFields = ["formatVersion", "serial", "applicationCode", "licenseId", "bindingId", "features", "issuedAt", "notBefore", "expiresAt", "deviceHash"];
    private readonly IReadOnlyDictionary<string, string> trustedKeys;
    private readonly ITrustedTimeStore store;
    private readonly TimeProvider clock;
    public OfflineLicenseVerifier(IReadOnlyDictionary<string, string> trustedPublicKeys, ITrustedTimeStore store, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(store); this.store = store; this.clock = clock ?? TimeProvider.System;
        if (trustedPublicKeys is null || trustedPublicKeys.Count == 0) throw new OfflineLicenseException("OFFLINE_KEYS_UNCONFIGURED");
        if (trustedPublicKeys.Count > 32) throw new OfflineLicenseException("OFFLINE_KEY_CONFIGURATION_INVALID");
        trustedKeys = trustedPublicKeys.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var entry in trustedKeys) using (ReadKey(entry.Key, entry.Value)) { }
    }

    public OfflineLicenseSession Open(string content, string applicationCode, string deviceId, IEnumerable<string>? requiredFeatures = null)
    {
        var payload = VerifySignature(content);
        if (applicationCode is null || payload.ApplicationCode != applicationCode.Trim().ToLowerInvariant()) throw new OfflineLicenseException("OFFLINE_APPLICATION_MISMATCH");
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId.Length > 256) throw new OfflineLicenseException("OFFLINE_DEVICE_MISMATCH");
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.ApplicationCode + "\n" + deviceId)));
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(payload.DeviceHash))) throw new OfflineLicenseException("OFFLINE_DEVICE_MISMATCH");
        var session = new OfflineLicenseSession(payload, store, clock);
        session.Demand();
        foreach (var feature in requiredFeatures ?? []) session.Demand(feature);
        return session;
    }

    private OfflinePayload VerifySignature(string content)
    {
        try
        {
            if (content is null || content.Length is < 100 or > 16384) throw Invalid();
            var parts = content.Split('.'); if (parts.Length != 3) throw Invalid();
            using var header = JsonDocument.Parse(Decode(parts[0]));
            RequireFields(header.RootElement, ["alg", "typ", "kid"]);
            if (header.RootElement.GetProperty("alg").GetString() != "ES256" || header.RootElement.GetProperty("typ").GetString() != "KG-OFFLINE") throw Invalid();
            var kid = header.RootElement.GetProperty("kid").GetString();
            if (kid is null || !trustedKeys.TryGetValue(kid, out var pem)) throw new OfflineLicenseException("OFFLINE_KEY_UNKNOWN");
            using var key = ReadKey(kid, pem); var signature = Decode(parts[2]);
            if (signature.Length != 64 || !key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) throw Invalid();
            using var body = JsonDocument.Parse(Decode(parts[1])); RequireFields(body.RootElement, PayloadFields);
            var payload = body.Deserialize<OfflinePayload>(Json) ?? throw Invalid();
            if (payload.FormatVersion != 1 || payload.Serial == Guid.Empty || payload.LicenseId == Guid.Empty || payload.BindingId == Guid.Empty ||
                payload.ApplicationCode is null || !Regex.IsMatch(payload.ApplicationCode, "\\A[a-z0-9_-]{3,64}\\z", RegexOptions.CultureInvariant) ||
                payload.DeviceHash is null || payload.DeviceHash.Length != 64 || !payload.DeviceHash.All(Uri.IsHexDigit) ||
                payload.Features is null || payload.Features.Length > 50 || payload.Features.Any(x => x is null || !Regex.IsMatch(x, "\\A[a-z][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)) ||
                payload.Features.Distinct(StringComparer.Ordinal).Count() != payload.Features.Length ||
                payload.IssuedAt > payload.NotBefore || payload.NotBefore >= payload.ExpiresAt || payload.ExpiresAt - payload.IssuedAt > TimeSpan.FromHours(168)) throw Invalid();
            return payload;
        }
        catch (Exception error) when (error is JsonException or FormatException or CryptographicException or KeyNotFoundException or InvalidOperationException or ArgumentException or OverflowException)
        { throw Invalid(); }
    }

    private static ECDsa ReadKey(string id, string pem)
    {
        var key = ECDsa.Create();
        try
        {
            if (id is null || !Regex.IsMatch(id, "\\A[a-zA-Z0-9._-]{1,64}\\z", RegexOptions.CultureInvariant) || pem is null || pem.Length > 8192 ||
                !pem.TrimStart().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal) || pem.Contains("PRIVATE KEY", StringComparison.Ordinal)) throw new CryptographicException();
            key.ImportFromPem(pem);
            if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7") throw new CryptographicException();
            return key;
        }
        catch (Exception error) when (error is CryptographicException or ArgumentException or InvalidOperationException)
        { key.Dispose(); throw new OfflineLicenseException("OFFLINE_KEY_CONFIGURATION_INVALID"); }
    }
    private static void RequireFields(JsonElement root, HashSet<string> expected)
    {
        if (root.ValueKind != JsonValueKind.Object) throw Invalid(); var fields = root.EnumerateObject().Select(x => x.Name).ToArray();
        if (fields.Length != expected.Count || !expected.SetEquals(fields)) throw Invalid();
    }
    private static byte[] Decode(string segment)
    {
        if (segment.Length == 0 || segment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw Invalid();
        var bytes = Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/') + new string('=', (4 - segment.Length % 4) % 4));
        if (Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_') != segment) throw Invalid(); return bytes;
    }
    private static OfflineLicenseException Invalid() => new("OFFLINE_LICENSE_INVALID");
}

internal sealed record OfflinePayload(int FormatVersion, Guid Serial, string ApplicationCode, Guid LicenseId, Guid BindingId,
    string[] Features, DateTimeOffset IssuedAt, DateTimeOffset NotBefore, DateTimeOffset ExpiresAt, string DeviceHash);

public static class TrustedOfflineKeys
{
    // This flat JSON dictionary is exported by the administration UI. Deliver
    // it separately over a trusted channel; never read keys from a certificate.
    public static IReadOnlyDictionary<string, string> Load(string path)
    {
        try
        {
            using var input = File.OpenRead(path); if (input.Length is < 1 or > 262144) throw new InvalidDataException();
            using var json = JsonDocument.Parse(input);
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in json.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || !result.TryAdd(property.Name, property.Value.GetString()!)) throw new InvalidDataException();
            }
            return result;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { throw new OfflineLicenseException("OFFLINE_KEY_CONFIGURATION_INVALID"); }
    }
}

