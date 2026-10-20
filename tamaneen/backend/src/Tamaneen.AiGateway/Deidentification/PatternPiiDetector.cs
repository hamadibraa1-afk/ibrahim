using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Tamaneen.AiGateway.Deidentification;

/// <summary>
/// In-process recognizers for Arabic and English. Deterministic and fast, so they always run, even when the
/// optional Presidio sidecar is down. Biased toward removing too much: a wrongly hidden word costs a little
/// fluency, a leaked phone number costs a patient's privacy.
/// </summary>
public sealed partial class PatternPiiDetector : IPiiDetector
{
    private static readonly Lazy<HashSet<string>> ArabicNames = new(() => LoadNames("names.ar.txt", ArabicKey));
    private static readonly Lazy<HashSet<string>> EnglishNames = new(() => LoadNames("names.en.txt", s => s.ToLowerInvariant()));

    // Common words that are also given names; they count as names only after a kinship word or title.
    private static readonly HashSet<string> AmbiguousArabic = new(new[] { "امل", "سعيد", "كريم", "جميل", "نور", "فرح", "صالح", "سلام", "حسن", "منى", "هدى", "وفاء", "صبر", "رجاء", "ايمان", "شروق", "سماح", "ريم", "ندى", "سهل", "عادل", "امين", "حنان", "راشد", "فهد", "ماجد", "منير", "نبيل" }.Select(ArabicKey));

    public Task<IReadOnlyList<PiiSpan>> DetectAsync(string text, string language, KnownIdentifiers known, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(known);
        var spans = new List<PiiSpan>();
        Add(spans, Email(), text, PiiType.Email, 0.99);
        Add(spans, Url(), text, PiiType.Url, 0.95);
        Add(spans, Iban(), text, PiiType.Iban, 0.95);
        Add(spans, Ip(), text, PiiType.IpAddress, 0.9);
        Add(spans, IsoDate(), text, PiiType.Date, 0.8);
        Add(spans, SlashDate(), text, PiiType.Date, 0.8);
        foreach (Match m in DigitRun().Matches(text))
        {
            var digits = Digits(m.Value);
            var type = digits.Length switch
            {
                >= 13 and <= 19 when Luhn(digits) => PiiType.CreditCard,
                10 when digits[0] is '1' or '2' => PiiType.NationalId,
                >= 7 and <= 15 => PiiType.Phone,
                _ => (PiiType?)null,
            };
            // A date, IP or IBAN already covers these digits.
            if (type is { } t && !spans.Any(s => s.Start < m.Index + m.Length && m.Index < s.End))
            {
                spans.Add(new PiiSpan(m.Index, m.Length, t, 0.9));
            }
        }

        Add(spans, ArabicAddress(), text, PiiType.Address, 0.75);
        Add(spans, EnglishAddress(), text, PiiType.Address, 0.75);
        AddKnown(spans, text, known.Names, PiiType.Person);
        AddKnown(spans, text, known.Places, PiiType.Address);
        AddContextualNames(spans, text);
        AddGazetteerNames(spans, text);
        return Task.FromResult<IReadOnlyList<PiiSpan>>(spans);
    }

    /// <summary>Folds Arabic spelling variants so "أحمد" and "احمد" compare equal.</summary>
    internal static string ArabicKey(string word)
    {
        var builder = new StringBuilder(word.Length);
        foreach (var ch in word.Normalize(NormalizationForm.FormKC))
        {
            if (ch is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ')
            {
                continue;
            }

            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return builder.ToString();
    }

    private static void AddKnown(List<PiiSpan> spans, string text, IReadOnlyList<string> values, PiiType type)
    {
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            // Each word of a known name is hidden on its own too ("نورة محمد" also hides a lone "نورة").
            foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length >= 2).Prepend(value.Trim()).Distinct())
            {
                foreach (var (start, length) in FindWord(text, part))
                {
                    spans.Add(new PiiSpan(start, length, type, 1.0));
                }
            }
        }
    }

    /// <summary>Words that follow "اسمي", "أخوي", "Dr.", "my sister" … are names, whatever they are.</summary>
    private static void AddContextualNames(List<PiiSpan> spans, string text)
    {
        foreach (Match m in ArabicNameCue().Matches(text))
        {
            var g = m.Groups["name"];
            var length = g.Length;
            var space = g.Value.IndexOf(' ', StringComparison.Ordinal);
            if (space > 0 && !IsArabicSurname(g.Value[(space + 1)..]))
            {
                // "أختي سارة رقمها" — the second word is ordinary text, not part of the name.
                length = space;
            }

            spans.Add(new PiiSpan(g.Index, length, PiiType.Person, 0.85));
        }

        foreach (Match m in EnglishNameCue().Matches(text))
        {
            var g = m.Groups["name"];
            spans.Add(new PiiSpan(g.Index, g.Length, PiiType.Person, 0.85));
        }
    }

    /// <summary>Common given names anywhere in the text, except names that are also everyday words.</summary>
    private static void AddGazetteerNames(List<PiiSpan> spans, string text)
    {
        foreach (Match m in Word().Matches(text))
        {
            var word = m.Value;
            if (word[0] is >= '؀' and <= 'ۿ')
            {
                var key = ArabicKey(StripArabicClitic(word));
                if (ArabicNames.Value.Contains(key) && !AmbiguousArabic.Contains(key))
                {
                    spans.Add(new PiiSpan(m.Index, m.Length, PiiType.Person, 0.7));
                }
            }
            else if (char.IsUpper(word[0]) && m.Index > 0 && EnglishNames.Value.Contains(word.ToLowerInvariant()))
            {
                spans.Add(new PiiSpan(m.Index, m.Length, PiiType.Person, 0.7));
            }
        }
    }

    /// <summary>A second name word: another given name (father's name) or a family nisba such as "القحطاني".</summary>
    private static bool IsArabicSurname(string word)
    {
        var key = ArabicKey(word);
        return ArabicNames.Value.Contains(key) || (key.StartsWith("ال", StringComparison.Ordinal) && key.EndsWith('ي') && key.Length >= 5);
    }

    private static string StripArabicClitic(string word) =>
        word.Length > 3 && (word[0] is 'و' or 'ل' or 'ب') && ArabicNames.Value.Contains(ArabicKey(word[1..])) ? word[1..] : word;

    private static IEnumerable<(int Start, int Length)> FindWord(string text, string value)
    {
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var before = index == 0 || !char.IsLetter(text[index - 1]) || text[index - 1] is 'و' or 'ل' or 'ب';
            var after = index + value.Length >= text.Length || !char.IsLetter(text[index + value.Length]);
            if (before && after)
            {
                yield return (index, value.Length);
            }

            index += value.Length;
        }
    }

    private static void Add(List<PiiSpan> spans, Regex regex, string text, PiiType type, double score)
    {
        foreach (Match m in regex.Matches(text))
        {
            spans.Add(new PiiSpan(m.Index, m.Length, type, score));
        }
    }

    private static string Digits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            var d = CharUnicodeInfo.GetDecimalDigitValue(ch);
            if (d >= 0)
            {
                builder.Append((char)('0' + d));
            }
        }

        return builder.ToString();
    }

    private static bool Luhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
            doubleIt = !doubleIt;
        }

        return sum % 10 == 0;
    }

    private static HashSet<string> LoadNames(string resource, Func<string, string> key)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new FileNotFoundException(resource);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var name = line.Trim();
            if (name.Length > 0 && !name.StartsWith('#'))
            {
                set.Add(key(name));
            }
        }

        return set;
    }

    [GeneratedRegex(@"[\p{L}\p{N}._%+-]+@[\p{L}\p{N}.-]+\.[\p{L}]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    [GeneratedRegex(@"\b(?:https?://|www\.)\S+|\b[\w-]+\.(?:com|net|org|sa|ae|io|me|app|info)(?:/\S*)?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b[A-Z]{2}\d{2}(?:[ ]?[A-Z0-9]{4}){3,7}(?:[ ]?[A-Z0-9]{1,3})?\b", RegexOptions.CultureInvariant)]
    private static partial Regex Iban();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ip();

    [GeneratedRegex(@"\b(?:19|20)\d{2}-\d{1,2}-\d{1,2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IsoDate();

    [GeneratedRegex(@"\b\d{1,2}[/.]\d{1,2}[/.](?:19|20)?\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex SlashDate();

    // Seven or more digits (Latin, Arabic-Indic or Persian), allowing +, spaces, dots and dashes between groups.
    [GeneratedRegex(@"(?<![\d٠-٩۰-۹])\+?[\d٠-٩۰-۹](?:[\s.\-()]*[\d٠-٩۰-۹]){6,18}(?![\d٠-٩۰-۹])", RegexOptions.CultureInvariant)]
    private static partial Regex DigitRun();

    [GeneratedRegex(@"(?:شارع|طريق|حي|حارة|مجمع|عمارة|فيلا|شقة|بناية|ص\.ب)\s+[\p{L}\p{N}]+(?:\s+[\p{N}]+)?", RegexOptions.CultureInvariant)]
    private static partial Regex ArabicAddress();

    [GeneratedRegex(@"\b\d{1,5}\s+(?:[A-Z][a-z]+\s+){1,3}(?:Street|St|Road|Rd|Avenue|Ave|Lane|Ln|Boulevard|Blvd|Drive|Dr)\b\.?", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishAddress();

    [GeneratedRegex(@"(?<!\p{L})[وف]?(?:اسمي|إسمي|يسموني|ينادوني|أخوي|اخوي|أختي|اختي|زوجي|زوجتي|ولدي|بنتي|صديقي|صديقتي|خويي|خويتي|أبو|ابو|أم|ام|دكتور|دكتورة|د\.|أستاذ|استاذ|أستاذة|استاذة|الأستاذ|الاستاذ|الدكتور|الدكتورة)\s+(?<name>[ء-ي]{2,}(?:\s+[ء-ي]{2,}(?=\s|$|[،,.!؟?]))?)", RegexOptions.CultureInvariant)]
    private static partial Regex ArabicNameCue();

    [GeneratedRegex(@"(?:\b(?:[Mm]y name is|[Cc]all me|[Mm]y (?:brother|sister|husband|wife|son|daughter|friend|boss|mother|father|mom|dad|uncle|aunt|cousin|boyfriend|girlfriend)|Dr\.?|Mr\.?|Mrs\.?|Ms\.?|Miss)\s+)(?<name>[A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex EnglishNameCue();

    [GeneratedRegex(@"[\p{L}]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Word();
}
