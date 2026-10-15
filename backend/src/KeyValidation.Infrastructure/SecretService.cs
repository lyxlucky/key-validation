using KeyValidation.Application;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KeyValidation.Domain;

namespace KeyValidation.Infrastructure;

public sealed class SecretService : ISecretService
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private readonly string current;
    private readonly Dictionary<string, byte[]> keys;
    public SecretService(IConfiguration configuration)
    {
        current = configuration["Licensing:CurrentKeyVersion"]?.ToLowerInvariant() ?? throw new InvalidOperationException("Licensing key configuration required.");
        keys = configuration.GetSection("Licensing:Keys").GetChildren().ToDictionary(x => x.Key.ToLowerInvariant(), x => Convert.FromBase64String(x.Value ?? ""));
        if (!keys.ContainsKey(current) || keys.Any(x => !Regex.IsMatch(x.Key, "^v[1-9][0-9]{0,3}$") || x.Value.Length < 32)) throw new InvalidOperationException("Licensing keys must use v-number versions and at least 32 bytes.");
    }
    public GeneratedKey GenerateLicense()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var chars = new StringBuilder(32); var buffer = 0; var bits = 0;
        foreach (var b in bytes)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { bits -= 5; chars.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        var body = chars.ToString();
        var plaintext = current.ToUpperInvariant() + "-" + string.Join("-", Enumerable.Range(0, 8).Select(i => body.Substring(i * 4, 4)));
        return new(plaintext, DigestLicense(plaintext).Digest, current, current.ToUpperInvariant() + "-…" + body[^4..]);
    }
    public (string Digest, string Version) DigestLicense(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 128) throw new RuleException("LICENSE_INVALID");
        var compact = new string(key.Where(c => c is not '-' and not ' ' and not '\t' and not '\r' and not '\n').ToArray()).ToUpperInvariant();
        var match = Regex.Match(compact, "^(V[1-9][0-9]{0,3})([0123456789ABCDEFGHJKMNPQRSTVWXYZ]{32})$", RegexOptions.CultureInvariant);
        if (!match.Success || !keys.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var secret)) throw new RuleException("LICENSE_INVALID");
        return (Convert.ToHexString(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(compact))), match.Groups[1].Value.ToLowerInvariant());
    }
    public string DigestDevice(Guid applicationId, string deviceId) => Hash(applicationId.ToString("N") + ":" + deviceId);
    public (string Token, string Digest) GenerateSession()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, DigestToken(token));
    }
    public string DigestToken(string token) => Hash(token);
    private static string Hash(string input) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
