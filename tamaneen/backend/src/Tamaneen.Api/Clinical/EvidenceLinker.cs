using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Tamaneen.Api.Clinical;

/// <summary>One turn of the intake conversation as the evidence inspector sees it.</summary>
public sealed record TranscriptTurn(Guid Id, int Turn, bool FromPatient, string Text);

/// <summary>
/// Links each statement of the intake summary to the patient messages it came from (S02, EC-15). The model is asked
/// to end each paragraph with the numbers of the patient lines it used ("[4, 8]"); those numbers become message ids.
/// When the model gave none (or for summaries written before citations), the patient messages that share the most
/// words with the statement are used instead. A statement with no source at all is never shown to the therapist.
/// </summary>
public static partial class EvidenceLinker
{
    public const int MaxSources = 4;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "you", "your", "with", "that", "this", "have", "has", "was", "are", "but", "not", "they", "them", "from", "about", "been", "when", "what",
        "في", "من", "على", "الى", "إلى", "عن", "مع", "هذا", "هذه", "ذلك", "التي", "الذي", "انت", "أنت", "انك", "أنك", "كان", "كانت", "لكن", "او", "أو", "منذ", "عند", "قبل", "بعد", "لديك", "عندك",
    };

    /// <summary>Numbers every turn ("[3] Patient: …") the way the summary and hypotheses prompts expect.</summary>
    public static string NumberedTranscript(IReadOnlyList<TranscriptTurn> turns) =>
        string.Join("\n", turns.Select(t => $"[{t.Turn}] {(t.FromPatient ? "Patient" : "Assistant")}: {t.Text}"));

    /// <summary>Removes a trailing "[4, 8]" citation and returns the cited turn numbers.</summary>
    public static (string Text, IReadOnlyList<int> Turns) SplitCitation(string paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        var match = Citation().Match(paragraph);
        if (!match.Success)
        {
            return (paragraph.Trim(), []);
        }

        var turns = match.Groups[1].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1)
            .Where(v => v > 0).Distinct().ToList();
        return (paragraph[..match.Index].Trim(), turns);
    }

    /// <summary>Cited turns that are really patient messages, as message ids.</summary>
    public static List<Guid> Resolve(IEnumerable<int> turns, IReadOnlyList<TranscriptTurn> transcript)
    {
        var byTurn = transcript.Where(t => t.FromPatient).ToDictionary(t => t.Turn);
        return turns.Where(byTurn.ContainsKey).Select(t => byTurn[t].Id).Distinct().Take(MaxSources).ToList();
    }

    /// <summary>Patient messages sharing words with the statement, best first.</summary>
    public static List<Guid> MatchLexically(string statement, IReadOnlyList<TranscriptTurn> transcript)
    {
        var words = Words(statement);
        if (words.Count == 0)
        {
            return [];
        }

        var needed = words.Count <= 4 ? 1 : 2;
        return transcript.Where(t => t.FromPatient)
            .Select(t => (t.Id, t.Turn, Score: Words(t.Text).Count(words.Contains)))
            .Where(x => x.Score >= needed)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Turn)
            .Take(MaxSources)
            .OrderBy(x => x.Turn)
            .Select(x => x.Id)
            .ToList();
    }

    /// <summary>Stored sources when there are any, otherwise the lexical match.</summary>
    public static List<Guid> SourcesFor(string statement, IReadOnlyList<Guid>? stored, IReadOnlyList<TranscriptTurn> transcript)
    {
        if (stored is { Count: > 0 })
        {
            return stored.ToList();
        }

        return MatchLexically(statement, transcript);
    }

    internal static HashSet<string> Words(string text)
    {
        var folded = Fold(text);
        return WordPattern().Matches(folded).Select(m => m.Value).Where(w => w.Length >= 3 && !StopWords.Contains(w)).Select(Stem).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Lower case, no diacritics, and the Arabic letter variants (أ إ آ → ا, ة → ه, ى → ي) folded together.</summary>
    private static string Fold(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(ch switch
            {
                'أ' or 'إ' or 'آ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                _ => char.ToLowerInvariant(ch),
            });
        }

        return builder.ToString();
    }

    /// <summary>Drops the commonest Arabic prefixes (ال، و، ب) and English plural "s" so "بضغط" meets "ضغط".</summary>
    private static string Stem(string word)
    {
        foreach (var prefix in new[] { "وال", "بال", "ال", "و", "ب" })
        {
            if (word.StartsWith(prefix, StringComparison.Ordinal) && word.Length - prefix.Length >= 3)
            {
                return word[prefix.Length..];
            }
        }

        return word.Length > 4 && word.EndsWith('s') ? word[..^1] : word;
    }

    [GeneratedRegex(@"\[\s*(\d+(?:\s*,\s*\d+)*)\s*\]\s*\.?\s*$")]
    private static partial Regex Citation();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}
