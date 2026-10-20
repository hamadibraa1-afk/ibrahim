using System.Security.Cryptography;

namespace Tamaneen.Api.Auth;

/// <summary>
/// Calm, gender-agreeing nickname suggestions ("نخلة هادئة" / "Calm Palm"), so nobody has to invent a name
/// or reach for their real one (P02).
/// </summary>
public static class Nicknames
{
    private static readonly (string Ar, string En)[] FeminineNouns =
    [
        ("نخلة", "Palm"), ("غيمة", "Cloud"), ("نجمة", "Star"), ("موجة", "Wave"), ("زهرة", "Flower"), ("واحة", "Oasis"),
        ("نسمة", "Breeze"), ("شجرة", "Tree"), ("قمرة", "Moonlight"), ("حمامة", "Dove"), ("فراشة", "Butterfly"), ("سحابة", "Mist"),
    ];

    private static readonly (string Ar, string En)[] FeminineAdjectives =
    [
        ("هادئة", "Calm"), ("لطيفة", "Gentle"), ("صافية", "Clear"), ("دافئة", "Warm"), ("مضيئة", "Bright"), ("ساكنة", "Still"),
        ("خضراء", "Green"), ("صبورة", "Patient"), ("رقيقة", "Soft"), ("بعيدة", "Distant"),
    ];

    private static readonly (string Ar, string En)[] MasculineNouns =
    [
        ("قمر", "Moon"), ("نهر", "River"), ("جبل", "Mountain"), ("غصن", "Branch"), ("فجر", "Dawn"), ("بحر", "Sea"),
        ("ضوء", "Light"), ("طائر", "Bird"), ("نسيم", "Wind"), ("سهل", "Meadow"),
    ];

    private static readonly (string Ar, string En)[] MasculineAdjectives =
    [
        ("هادئ", "Calm"), ("لطيف", "Gentle"), ("صافٍ", "Clear"), ("دافئ", "Warm"), ("مضيء", "Bright"), ("ساكن", "Still"),
        ("أخضر", "Green"), ("صبور", "Patient"), ("بعيد", "Distant"), ("رقيق", "Soft"),
    ];

    public static string Suggest(string language)
    {
        var feminine = RandomNumberGenerator.GetInt32(2) == 0;
        var nouns = feminine ? FeminineNouns : MasculineNouns;
        var adjectives = feminine ? FeminineAdjectives : MasculineAdjectives;
        var noun = nouns[RandomNumberGenerator.GetInt32(nouns.Length)];
        var adjective = adjectives[RandomNumberGenerator.GetInt32(adjectives.Length)];
        return language == "en" ? $"{adjective.En} {noun.En}" : $"{noun.Ar} {adjective.Ar}";
    }

    /// <summary>2–40 characters, no e-mail address and no long digit runs (a nickname is not a phone number).</summary>
    public static bool IsAcceptable(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return false;
        }

        var value = nickname.Trim();
        var digits = value.Count(char.IsDigit);
        return value.Length is >= 2 and <= 40 && !value.Contains('@', StringComparison.Ordinal) && digits < 5 && !value.Any(char.IsControl);
    }
}
