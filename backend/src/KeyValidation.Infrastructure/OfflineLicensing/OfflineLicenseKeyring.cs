using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using KeyValidation.Application;
using KeyValidation.Domain;
using Microsoft.Extensions.Configuration;

namespace KeyValidation.Infrastructure;
// Signing keys are supplied by the operator. This class never generates or
// returns private material, including when configuration is incomplete.
public sealed class OfflineLicenseKeyring(IConfiguration configuration) : IOfflineLicenseSigner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false
    };
    public OfflineConfigurationDto Configuration()
    {
        var enabled = !bool.TryParse(configuration["Offline:Enabled"], out var value) || value;
        try
        {
            using var keys = Load();
            return new(enabled, true, keys.CurrentId, keys.Values.ToDictionary(x => x.Key, x => x.Value.ExportSubjectPublicKeyInfoPem(), StringComparer.Ordinal), null, OfflineLicenseLimits.MaximumHours, OfflineLicenseLimits.DefaultHours, OfflineLicenseLimits.Limitations);
        }
        catch (RuleException error)
        {
            return new(enabled, false, null, new Dictionary<string, string>(), error.Code, OfflineLicenseLimits.MaximumHours, OfflineLicenseLimits.DefaultHours, OfflineLicenseLimits.Limitations);
        }
    }

    public (string Content, string KeyId) Sign(OfflineLicensePayload payload)
    {
        using var keys = Load();
        var header = Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", typ = "KG-OFFLINE", kid = keys.CurrentId }, Json));
        var body = Encode(JsonSerializer.SerializeToUtf8Bytes(payload, Json));
        var input = header + "." + body;
        var signature = keys.Values[keys.CurrentId].SignData(Encoding.ASCII.GetBytes(input), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (input + "." + Encode(signature), keys.CurrentId);
    }

    public OfflineLicensePayload Verify(string content)
    {
        using var keys = Load();
        try
        {
            if (content is null || content.Length is < 100 or > 16384)
                throw Invalid();
            var parts = content.Split('.');
            if (parts.Length != 3)
                throw Invalid();
            using var header = JsonDocument.Parse(Decode(parts[0]));
            if (header.RootElement.ValueKind != JsonValueKind.Object)
                throw Invalid();
            var properties = header.RootElement.EnumerateObject().ToArray();
            if (properties.Length != 3 || properties.Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 3 || header.RootElement.GetProperty("alg").GetString() != "ES256" || header.RootElement.GetProperty("typ").GetString() != "KG-OFFLINE")
                throw Invalid();
            var kid = header.RootElement.GetProperty("kid").GetString();
            if (kid is null || !keys.Values.TryGetValue(kid, out var key))
                throw new RuleException("OFFLINE_KEY_UNKNOWN");
            var signature = Decode(parts[2]);
            if (signature.Length != 64 || !key.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                throw Invalid();
            using var body = JsonDocument.Parse(Decode(parts[1]));
            if (body.RootElement.ValueKind != JsonValueKind.Object || body.RootElement.EnumerateObject().Select(x => x.Name).Distinct(StringComparer.Ordinal).Count() != 10 || body.RootElement.EnumerateObject().Count() != 10)
                throw Invalid();
            var payload = body.Deserialize<OfflineLicensePayload>(Json) ?? throw Invalid();
            if (payload.FormatVersion != 1 || payload.Serial == Guid.Empty || payload.LicenseId == Guid.Empty || payload.BindingId == Guid.Empty || payload.ApplicationCode is null || !Regex.IsMatch(payload.ApplicationCode, "\\A[a-z0-9_-]{3,64}\\z", RegexOptions.CultureInvariant) || payload.DeviceHash is null || payload.DeviceHash.Length != 64 || !payload.DeviceHash.All(Uri.IsHexDigit) || payload.Features is null || payload.Features.Length > 50 || payload.Features.Any(x => x is null || !Regex.IsMatch(x, "\\A[a-z][a-z0-9._-]{0,63}\\z", RegexOptions.CultureInvariant)) || payload.Features.Distinct(StringComparer.Ordinal).Count() != payload.Features.Length || payload.IssuedAt > payload.NotBefore || payload.NotBefore >= payload.ExpiresAt || payload.ExpiresAt - payload.IssuedAt > TimeSpan.FromHours(168))
                throw Invalid();
            return payload;
        }
        catch (Exception error)when (error is JsonException or FormatException or CryptographicException or KeyNotFoundException or InvalidOperationException or ArgumentException or OverflowException)
        {
            throw Invalid();
        }
    }

    private KeySet Load()
    {
        if (configuration["Offline:Enabled"] is { } enabled)
        {
            if (!bool.TryParse(enabled, out var value))
                throw new RuleException("OFFLINE_KEY_CONFIGURATION_INVALID", 503);
            if (!value)
                throw new RuleException("OFFLINE_DISABLED", 503);
        }

        var current = configuration["Offline:CurrentKeyId"];
        var entries = configuration.GetSection("Offline:Keys").GetChildren().ToArray();
        if (string.IsNullOrWhiteSpace(current) || entries.Length == 0)
            throw new RuleException("OFFLINE_UNCONFIGURED", 503);
        var result = new KeySet(current);
        try
        {
            if (!ValidId(current) || entries.Length > 32)
                throw new CryptographicException();
            foreach (var entry in entries)
            {
                if (!ValidId(entry.Key) || entry.Value is null || entry.Value.Length > 8192 || !(entry.Value.TrimStart().StartsWith("-----BEGIN PRIVATE KEY-----", StringComparison.Ordinal) || entry.Value.TrimStart().StartsWith("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal)))
                    throw new CryptographicException();
                var key = ECDsa.Create();
                result.Values.Add(entry.Key, key);
                key.ImportFromPem(entry.Value);
                var parameters = key.ExportParameters(false);
                if (key.KeySize != 256 || parameters.Curve.Oid.Value != "1.2.840.10045.3.1.7")
                    throw new CryptographicException();
            }

            if (!result.Values.TryGetValue(current, out var signingKey))
                throw new CryptographicException();
            var privateParameters = signingKey.ExportParameters(true);
            try
            {
                if (privateParameters.D is not { Length: 32 })
                    throw new CryptographicException();
            }
            finally
            {
                if (privateParameters.D is not null)
                    CryptographicOperations.ZeroMemory(privateParameters.D);
            }

            return result;
        }
        catch (Exception error)when (error is CryptographicException or ArgumentException or InvalidOperationException)
        {
            result.Dispose();
            throw new RuleException("OFFLINE_KEY_CONFIGURATION_INVALID", 503);
        }
    }

    private sealed class KeySet(string currentId) : IDisposable
    {
        internal string CurrentId { get; } = currentId;
        internal Dictionary<string, ECDsa> Values { get; } = new(StringComparer.Ordinal);

        public void Dispose()
        {
            foreach (var key in Values.Values)
                key.Dispose();
        }
    }

    private static bool ValidId(string value) => Regex.IsMatch(value, "\\A[a-zA-Z0-9._-]{1,64}\\z", RegexOptions.CultureInvariant);
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Decode(string segment)
    {
        if (segment.Length == 0 || segment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw Invalid();
        var bytes = Convert.FromBase64String(segment.Replace('-', '+').Replace('_', '/') + new string ('=', (4 - segment.Length % 4) % 4));
        if (Encode(bytes) != segment)
            throw Invalid();
        return bytes;
    }

    private static RuleException Invalid() => new("OFFLINE_LICENSE_INVALID");
}
