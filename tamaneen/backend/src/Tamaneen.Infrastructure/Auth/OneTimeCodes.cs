using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Tamaneen.Infrastructure.Auth;

/// <summary>Six-digit codes sent by SMS or e-mail, and recovery codes in the XXXX-XXXX format.</summary>
public static class OneTimeCodes
{
    // Crockford-style alphabet without 0/O, 1/I/L, U: easy to read back from paper.
    private const string RecoveryAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string NewNumericCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static string NewRecoveryCode()
    {
        Span<char> chars = stackalloc char[9];
        for (var i = 0; i < 9; i++)
        {
            chars[i] = i == 4 ? '-' : RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
        }

        return new string(chars);
    }

    /// <summary>Digits only, with Arabic-Indic and Persian digits mapped to ASCII. Null if nothing usable.</summary>
    public static string? Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        var builder = new StringBuilder(code.Length);
        foreach (var ch in code)
        {
            var d = CharUnicodeInfo.GetDecimalDigitValue(ch);
            if (d >= 0)
            {
                builder.Append((char)('0' + d));
            }
            else if (!char.IsWhiteSpace(ch) && ch != '-')
            {
                return null;
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>Upper-cases and strips spaces/dashes so "7kq2 m9xa" matches "7KQ2-M9XA".</summary>
    public static string NormalizeRecovery(string? code) =>
        new string((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static byte[] RecoveryHash(Guid userId, string code) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{userId:N}:{NormalizeRecovery(code)}"));

    public static byte[] TokenHash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>"••••4521" or "s•••@example.com": enough for the user to recognize, not to learn.</summary>
    public static string Mask(string contact, string contactType)
    {
        if (contactType == "email")
        {
            var at = contact.IndexOf('@', StringComparison.Ordinal);
            return at <= 0 ? "•••" : $"{contact[0]}•••{contact[at..]}";
        }

        var digits = new string(contact.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? "••••" : "••••" + digits[^4..];
    }
}
