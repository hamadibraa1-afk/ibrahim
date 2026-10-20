using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Tamaneen.SharedKernel;

/// <summary>
/// A random, meaningless identifier shown to staff and sent to logs and the AI gateway instead of a name
/// or the database key. Format: P-XXXX-XXXX in Crockford base32 (no I, L, O, U), about 40 bits of randomness.
/// </summary>
public readonly partial record struct PseudonymId
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private PseudonymId(string value) => Value = value;

    public string Value { get; }

    public static PseudonymId New()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = Alphabet[bytes[i] & 31];
        }

        return new PseudonymId($"P-{new string(chars[..4])}-{new string(chars[4..])}");
    }

    public static PseudonymId Parse(string value)
    {
        if (!TryParse(value, out var id))
        {
            throw new DomainException(ErrorCodes.InvalidPseudonym, $"'{value}' is not a valid pseudonym id.");
        }

        return id;
    }

    public static bool TryParse(string? value, out PseudonymId id)
    {
        id = default;
        if (value is null)
        {
            return false;
        }

        var upper = value.Trim().ToUpperInvariant();
        if (!Pattern().IsMatch(upper))
        {
            return false;
        }

        id = new PseudonymId(upper);
        return true;
    }

    public override string ToString() => Value;

    [GeneratedRegex("^P-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
