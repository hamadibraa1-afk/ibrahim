using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Auth;

/// <summary>
/// PBKDF2-HMAC-SHA256 with 600,000 iterations and a 16-byte salt (OWASP 2023). Format:
/// pbkdf2-sha256$iterations$salt$hash. Passwords must be at least 12 characters and must not be on the
/// list of the most-leaked passwords, nor be built from the user's own nickname or contact.
/// </summary>
public sealed class PasswordService
{
    public const int MinLength = 12;
    public const int MaxLength = 128;
    private const int Iterations = 600_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;
    private const string Prefix = "pbkdf2-sha256";

    private static readonly Lazy<HashSet<string>> Common = new(LoadCommon);

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return string.Join('$', Prefix, Iterations.ToString(CultureInfo.InvariantCulture), Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public PasswordCheck Verify(string stored, string password)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations))
        {
            return PasswordCheck.Failed;
        }

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordCheck.Failed;
        }

        return iterations < Iterations ? PasswordCheck.SucceededRehashNeeded : PasswordCheck.Succeeded;
    }

    /// <summary>Burns the same CPU time as a real check, so unknown accounts answer as slowly as known ones.</summary>
    public void VerifyDummy(string password) =>
        Rfc2898DeriveBytes.Pbkdf2(password, new byte[SaltSize], Iterations, HashAlgorithmName.SHA256, HashSize);

    /// <summary>Throws <see cref="ErrorCodes.WeakPassword"/> when the password breaks the policy.</summary>
    public static void EnsureStrong(string? password, params string?[] personal)
    {
        if (!IsStrong(password, personal))
        {
            throw new DomainException(ErrorCodes.WeakPassword, "Password does not meet the policy.");
        }
    }

    public static bool IsStrong(string? password, params string?[] personal)
    {
        if (password is null || password.Length < MinLength || password.Length > MaxLength)
        {
            return false;
        }

        var lowered = password.ToLowerInvariant();
        if (Common.Value.Contains(lowered) || password.Distinct().Count() < 4)
        {
            return false;
        }

        // "aaaa…" or "1234567890ab" style sequences.
        var sequential = 0;
        for (var i = 1; i < password.Length; i++)
        {
            sequential = password[i] - password[i - 1] is 0 or 1 or -1 ? sequential + 1 : 0;
            if (sequential >= 7)
            {
                return false;
            }
        }

        foreach (var p in personal)
        {
            var value = p?.Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(value) && value.Length >= 4 && lowered.Contains(value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static HashSet<string> LoadCommon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("common-passwords.txt") ?? throw new FileNotFoundException("common-passwords.txt");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var value = line.Trim();
            if (value.Length > 0 && !value.StartsWith('#'))
            {
                set.Add(value.ToLowerInvariant());
            }
        }

        return set;
    }
}

public enum PasswordCheck
{
    Failed = 0,
    Succeeded = 1,
    SucceededRehashNeeded = 2,
}
