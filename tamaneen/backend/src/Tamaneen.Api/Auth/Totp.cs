using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Tamaneen.Api.Auth;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 6 digits, 30-second steps) as used by common authenticator apps.</summary>
public static class Totp
{
    public const int StepSeconds = 30;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(20);

    public static long StepAt(DateTimeOffset time) => time.ToUnixTimeSeconds() / StepSeconds;

    public static string Code(byte[] secret, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(secret, counter, hash);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the matching step within ±1 step of now (clock drift), or null.</summary>
    public static long? Verify(byte[] secret, string? code, DateTimeOffset now)
    {
        var digits = OneTimeCodes.Normalize(code);
        if (digits is not { Length: 6 })
        {
            return null;
        }

        var current = StepAt(now);
        long? match = null;
        for (var step = current - 1; step <= current + 1; step++)
        {
            // Compare every candidate in constant time; don't stop at the first match.
            if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(digits)))
            {
                match = step;
            }
        }

        return match;
    }

    public static string Base32(byte[] data)
    {
        var builder = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                builder.Append(Base32Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }

        if (bits > 0)
        {
            builder.Append(Base32Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return builder.ToString();
    }

    public static string OtpAuthUri(string base32Secret, string accountLabel) =>
        $"otpauth://totp/{Uri.EscapeDataString("Tamaneen:" + accountLabel)}?secret={base32Secret}&issuer=Tamaneen&algorithm=SHA1&digits=6&period={StepSeconds}";
}
