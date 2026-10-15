using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyValidation.Api;

internal static class Totp
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        var result = new StringBuilder(32);
        var buffer = 0;
        var bits = 0;
        foreach (var value in bytes)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5) { bits -= 5; result.Append(Alphabet[(buffer >> bits) & 31]); }
        }
        return result.ToString();
    }

    public static bool Verify(string secret, string code, DateTimeOffset now, long? lastStep, out long acceptedStep)
    {
        acceptedStep = -1;
        if (code is null || code.Length != 6 || code.Any(c => c is < '0' or > '9')) return false;
        var key = Decode(secret);
        var current = now.ToUnixTimeSeconds() / 30;
        // Prefer the greatest matching step so a rare adjacent-code collision cannot be replayed.
        for (var step = current + 1; step >= current - 1 && step >= 0; step--)
        {
            if (step <= (lastStep ?? -1)) continue;
            var counter = new byte[8];
            BinaryPrimitives.WriteInt64BigEndian(counter, step);
            var hash = HMACSHA1.HashData(key, counter);
            var offset = hash[^1] & 15;
            var value = BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7fffffff;
            var expected = (value % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(code)))
            { acceptedStep = step; return true; }
        }
        return false;
    }

    private static byte[] Decode(string value)
    {
        var result = new byte[value.Length * 5 / 8];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var character in value)
        {
            var digit = Alphabet.IndexOf(character);
            if (digit < 0) throw new CryptographicException("Invalid authenticator key encoding.");
            buffer = (buffer << 5) | digit;
            bits += 5;
            if (bits >= 8) { bits -= 8; result[index++] = (byte)(buffer >> bits); }
        }
        return result;
    }
}
