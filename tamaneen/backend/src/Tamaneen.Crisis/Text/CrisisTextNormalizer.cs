using System.Globalization;
using System.Text;

namespace Tamaneen.Crisis.Text;

/// <summary>
/// Puts patient text and rule patterns into one canonical form so that spelling variation does not hide risk.
/// The same normalizer is applied to both sides, so a rule is written once and still matches variants.
/// <list type="bullet">
/// <item>Unicode compatibility folding (presentation forms such as ﻻ) and lower-case Latin.</item>
/// <item>Arabic: diacritics and tatweel removed; أ إ آ ٱ → ا, ى ی → ي, ة → ه, ؤ → و, ئ → ي, ک گ → ك; Arabic-Indic digits → 0-9.</item>
/// <item>Arabizi: digits inside Latin words become letters (2→ removed, 3→a, 5→kh, 6→t, 7→h, 8→gh, 9→q).</item>
/// <item>A letter repeated for emphasis ("ابييييي", "pleeease") collapses to one.</item>
/// <item>Everything that is not a letter or digit separates words.</item>
/// </list>
/// </summary>
public static class CrisisTextNormalizer
{
    public static string Normalize(string? text) => string.Join(' ', Tokenize(text));

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var folded = text.Normalize(NormalizationForm.FormKC);
        var tokens = new List<string>();
        var word = new StringBuilder(16);
        foreach (var raw in folded)
        {
            var ch = Fold(raw);
            if (ch == '\0')
            {
                continue; // diacritic or tatweel: dropped without splitting the word
            }

            if (char.IsLetterOrDigit(ch))
            {
                word.Append(ch);
            }
            else
            {
                Flush(word, tokens);
            }
        }

        Flush(word, tokens);
        return tokens;
    }

    private static void Flush(StringBuilder word, List<string> tokens)
    {
        if (word.Length == 0)
        {
            return;
        }

        var token = MapArabizi(word.ToString());
        token = CollapseRepeats(token);
        if (token.Length > 0)
        {
            tokens.Add(token);
        }

        word.Clear();
    }

    private static char Fold(char ch)
    {
        switch (ch)
        {
            case 'ـ': // tatweel
                return '\0';
            case 'أ' or 'إ' or 'آ' or 'ٱ':
                return 'ا';
            case 'ى' or 'ی' or 'ئ':
                return 'ي';
            case 'ة':
                return 'ه';
            case 'ؤ':
                return 'و';
            case 'ک' or 'گ':
                return 'ك';
            case 'ء':
                return '\0';
        }

        if (ch is >= 'ً' and <= 'ٟ' or 'ٰ' or >= 'ۖ' and <= 'ۭ')
        {
            return '\0';
        }

        var digit = CharUnicodeInfo.GetDecimalDigitValue(ch);
        if (digit >= 0 && ch > '\u007F')
        {
            return (char)('0' + digit);
        }

        return char.ToLowerInvariant(ch);
    }

    /// <summary>Only words that mix Latin letters and digits are Arabizi; plain numbers ("911", "15") stay as they are.</summary>
    private static string MapArabizi(string token)
    {
        var hasLatin = false;
        var hasDigit = false;
        foreach (var ch in token)
        {
            hasLatin |= ch is >= 'a' and <= 'z';
            hasDigit |= ch is >= '0' and <= '9';
        }

        if (!hasLatin || !hasDigit)
        {
            return token;
        }

        var builder = new StringBuilder(token.Length + 4);
        foreach (var ch in token)
        {
            builder.Append(ch switch
            {
                '2' => string.Empty,
                '3' => "a",
                '5' => "kh",
                '6' => "t",
                '7' => "h",
                '8' => "gh",
                '9' => "q",
                _ => ch.ToString(),
            });
        }

        return builder.ToString();
    }

    private static string CollapseRepeats(string token)
    {
        if (token.Length < 2)
        {
            return token;
        }

        var builder = new StringBuilder(token.Length);
        var previous = '\0';
        foreach (var ch in token)
        {
            if (ch == previous && char.IsLetter(ch))
            {
                continue;
            }

            builder.Append(ch);
            previous = ch;
        }

        return builder.ToString();
    }
}
