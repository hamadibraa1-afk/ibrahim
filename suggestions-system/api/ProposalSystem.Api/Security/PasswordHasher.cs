using System.Globalization;
using System.Security.Cryptography;

namespace ProposalSystem.Api.Security;

/// <summary>
/// PBKDF2-HMAC-SHA256 with a per-password random salt.
/// Stored format: <c>PBKDF2$SHA256${iterations}${saltBase64}${hashBase64}</c>, so the work factor
/// can be raised later: hashes made with fewer iterations are upgraded on the next successful login.
/// </summary>
public static class PasswordHasher
{
    /// <summary>OWASP Password Storage Cheat Sheet recommendation for PBKDF2-HMAC-SHA256.</summary>
    public const int Iterations = 600_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Prefix = "PBKDF2$SHA256$";

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return string.Create(CultureInfo.InvariantCulture,
            $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}");
    }

    public static bool Verify(string password, string stored, out bool needsRehash)
    {
        needsRehash = false;
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var parts = stored.Split('$');
        if (parts.Length != 5 || !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations < 10_000)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[3]);
            expected = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        var ok = CryptographicOperations.FixedTimeEquals(actual, expected);
        needsRehash = ok && iterations < Iterations;
        return ok;
    }

    public static bool IsValidHash(string? stored) =>
        !string.IsNullOrEmpty(stored) && stored.StartsWith(Prefix, StringComparison.Ordinal) && stored.Split('$').Length == 5;

    /// <summary>At least 8 characters with both letters and digits.</summary>
    public static string? ValidatePolicy(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return "كلمة المرور يجب ألا تقل عن 8 أحرف.";
        if (password.Length > 128)
            return "كلمة المرور طويلة جداً.";
        if (!password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return "كلمة المرور يجب أن تحتوي على حروف وأرقام.";
        return null;
    }
}
