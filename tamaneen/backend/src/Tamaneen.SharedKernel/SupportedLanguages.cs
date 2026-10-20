using System.Globalization;

namespace Tamaneen.SharedKernel;

/// <summary>The two languages of the platform. Arabic is the default and is right-to-left.</summary>
public static class SupportedLanguages
{
    public const string Arabic = "ar";
    public const string English = "en";
    public const string Default = Arabic;

    public static readonly IReadOnlyList<string> All = new[] { Arabic, English };

    /// <summary>True only for Arabic or English (any region). Unknown codes are not silently accepted.</summary>
    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Neutral(code) is Arabic or English;

    /// <summary>Maps "ar-SA", "AR", "en_US" and similar to the two-letter code; unknown input falls back to the default.</summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return Default;
        }

        var neutral = Neutral(code);
        return neutral is Arabic or English ? neutral : Default;
    }

    private static string Neutral(string code) => code.Trim().Replace('_', '-').Split('-')[0].ToLowerInvariant();

    public static bool IsRightToLeft(string code) => Normalize(code) == Arabic;

    public static string Direction(string code) => IsRightToLeft(code) ? "rtl" : "ltr";

    public static CultureInfo Culture(string code) => CultureInfo.GetCultureInfo(Normalize(code));
}
