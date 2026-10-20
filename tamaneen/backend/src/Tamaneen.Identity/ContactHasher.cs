using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tamaneen.Identity;

/// <summary>
/// HMAC-SHA256 with a secret pepper held in Key Vault. Input is normalized first so that the same
/// person always produces the same hash: emails are trimmed and lower-cased, phone numbers keep digits
/// only, with Arabic-Indic and Persian digits mapped to ASCII and a leading 00 turned into +.
/// </summary>
public sealed class ContactHasher : IContactHasher
{
    private readonly byte[] _pepper;

    public ContactHasher(byte[] pepper)
    {
        if (pepper is not { Length: >= 32 })
        {
            throw new ArgumentException("The pepper must be at least 32 bytes.", nameof(pepper));
        }

        _pepper = pepper;
    }

    public byte[] Hash(string emailOrPhone)
    {
        var normalized = Normalize(emailOrPhone);
        return HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(normalized));
    }

    public static string Normalize(string emailOrPhone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(emailOrPhone);
        var value = emailOrPhone.Trim();
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return "e:" + value.ToLowerInvariant();
        }

        var builder = new StringBuilder(value.Length + 2);
        foreach (var ch in value)
        {
            var digit = CharUnicodeInfo.GetDecimalDigitValue(ch);
            if (digit >= 0)
            {
                builder.Append((char)('0' + digit));
            }
            else if (ch == '+' && builder.Length == 0)
            {
                builder.Append('+');
            }
        }

        var phone = builder.ToString();
        if (phone.StartsWith("00", StringComparison.Ordinal))
        {
            phone = "+" + phone[2..];
        }

        if (phone.Length < 6)
        {
            throw new ArgumentException("Not a valid email or phone number.", nameof(emailOrPhone));
        }

        return "p:" + phone;
    }
}
