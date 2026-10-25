using System.Security.Cryptography;
using System.Text;
using Tamaneen.Identity;
using Tamaneen.SharedKernel;

namespace Tamaneen.Api.Patients;

/// <summary>
/// Optimistic concurrency for the patient's own settings (PB-52): the profile (P31) and the safety info (P21, P34).
/// The GET answers with an ETag computed from the stored values, and the PUT must send it back in If-Match. Two
/// devices editing at once therefore cannot silently overwrite each other: the second save gets 412 and reloads.
/// No column is needed: every save re-encrypts the fields, so the ciphertext itself changes with each write.
/// </summary>
public static class SettingsVersion
{
    public static string Profile(PatientProfile profile) =>
        Tag("profile", profile.DisplayNameCipher, profile.TimeZone);

    public static string SafetyInfo(PatientProfile profile) =>
        Tag("safety", profile.EmergencyContactCipher, profile.ShareLocation ? "1" : "0", profile.ShareLocationSinceUtc?.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Sets the ETag response header (quoted, as RFC 9110 asks).</summary>
    public static void Send(HttpContext http, string version) => http.Response.Headers.ETag = $"\"{version}\"";

    /// <summary>428 without If-Match, 412 when it names another version. A weak validator (W/"…") is compared by its value.</summary>
    public static void Require(HttpContext http, string current)
    {
        var presented = http.Request.Headers.IfMatch.ToString().Trim();
        if (presented.Length == 0)
        {
            throw new DomainException(ErrorCodes.SettingsVersionRequired, "If-Match is required to save settings.");
        }

        if (presented == "*")
        {
            return;
        }

        var matches = presented.Split(',').Select(v => v.Trim()).Select(v => v.StartsWith("W/", StringComparison.Ordinal) ? v[2..] : v).Any(v => v.Trim('"') == current);
        if (!matches)
        {
            throw new DomainException(ErrorCodes.SettingsVersionChanged, "The settings changed since they were read.");
        }
    }

    private static string Tag(string kind, params object?[] parts)
    {
        var builder = new StringBuilder(kind);
        foreach (var part in parts)
        {
            builder.Append('|').Append(part switch
            {
                null => "-",
                byte[] bytes => Convert.ToBase64String(SHA256.HashData(bytes)),
                _ => part.ToString(),
            });
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())), 0, 12).ToLowerInvariant();
    }
}
