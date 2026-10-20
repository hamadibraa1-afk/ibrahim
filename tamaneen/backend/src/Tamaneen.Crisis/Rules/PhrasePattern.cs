using Tamaneen.Crisis.Text;

namespace Tamaneen.Crisis.Rules;

/// <summary>
/// A phrase written by clinicians in a small, safe pattern language (no regular expressions):
/// <list type="bullet">
/// <item>Words separated by spaces must appear in order.</item>
/// <item><c>a|b|c</c>: any one of the alternatives; <c>a_b</c> inside an alternative means the words a then b.</item>
/// <item><c>word*</c>: any word that starts with "word" ("انتحر*" matches انتحرت، انتحره).</item>
/// <item><c>word?</c>: the word may be missing.</item>
/// <item><c>...</c>: up to three other words in between.</item>
/// </list>
/// Arabic words also match with an attached clitic (و ف ب ل ك س and the article ال), so "اموت" matches "واموت" and "ساموت".
/// Every alternative is normalized with <see cref="CrisisTextNormalizer"/>, exactly like patient text.
/// </summary>
public sealed class PhrasePattern
{
    private const int MaxGap = 3;

    private static readonly string[] ArabicClitics =
    {
        "و", "ف", "ب", "ل", "ك", "س", "ال", "وال", "بال", "فال", "كال", "لل", "ولل", "فلل", "وب", "ول", "فب", "فل", "وس", "فس",
    };

    private readonly IReadOnlyList<Element> _elements;

    private PhrasePattern(string source, IReadOnlyList<Element> elements)
    {
        Source = source;
        _elements = elements;
    }

    public string Source { get; }

    public static PhrasePattern Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            throw new FormatException("A pattern cannot be empty.");
        }

        var elements = new List<Element>();
        foreach (var part in source.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "...")
            {
                if (elements.Count == 0 || elements[^1].IsGap)
                {
                    throw new FormatException($"'...' must follow a word in '{source}'.");
                }

                elements.Add(Element.Gap);
                continue;
            }

            var optional = part.EndsWith('?');
            var body = optional ? part[..^1] : part;
            var alternatives = new List<Alternative>();
            foreach (var alt in body.Split('|', StringSplitOptions.RemoveEmptyEntries))
            {
                var words = new List<WordSpec>();
                foreach (var piece in alt.Split('_', StringSplitOptions.RemoveEmptyEntries))
                {
                    var prefix = piece.EndsWith('*');
                    var word = prefix ? piece[..^1] : piece;
                    var tokens = CrisisTextNormalizer.Tokenize(word);
                    if (tokens.Count != 1)
                    {
                        throw new FormatException($"'{piece}' in '{source}' must be exactly one word after normalization.");
                    }

                    words.Add(new WordSpec(tokens[0], prefix));
                }

                if (words.Count == 0)
                {
                    throw new FormatException($"Empty alternative in '{source}'.");
                }

                alternatives.Add(new Alternative(words, IsArabic(words[0].Word)));
            }

            if (alternatives.Count == 0)
            {
                throw new FormatException($"Empty word in '{source}'.");
            }

            elements.Add(new Element(alternatives, optional, IsGap: false));
        }

        if (elements.Count == 0 || elements[^1].IsGap)
        {
            throw new FormatException($"'{source}' must end with a word.");
        }

        if (elements.All(e => e.Optional))
        {
            throw new FormatException($"'{source}' needs at least one required word.");
        }

        return new PhrasePattern(source, elements);
    }

    /// <summary>Returns the first matching span (start inclusive, end exclusive) in normalized tokens.</summary>
    public (int Start, int End)? Match(IReadOnlyList<string> tokens) => Matches(tokens).Select(m => ((int, int)?)m).FirstOrDefault();

    /// <summary>Every match, one per start position, so an excused match never hides a later one.</summary>
    public IEnumerable<(int Start, int End)> Matches(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        for (var start = 0; start < tokens.Count; start++)
        {
            var end = MatchFrom(tokens, start, 0);
            if (end > start)
            {
                yield return (start, end);
            }
        }
    }

    private int MatchFrom(IReadOnlyList<string> tokens, int position, int elementIndex)
    {
        if (elementIndex == _elements.Count)
        {
            return position;
        }

        var element = _elements[elementIndex];
        if (element.IsGap)
        {
            for (var skip = 0; skip <= MaxGap && position + skip <= tokens.Count; skip++)
            {
                var end = MatchFrom(tokens, position + skip, elementIndex + 1);
                if (end >= 0)
                {
                    return end;
                }
            }

            return -1;
        }

        foreach (var length in element.MatchLengths(tokens, position))
        {
            var end = MatchFrom(tokens, position + length, elementIndex + 1);
            if (end >= 0)
            {
                return end;
            }
        }

        if (element.Optional)
        {
            return MatchFrom(tokens, position, elementIndex + 1);
        }

        return -1;
    }

    private static bool IsArabic(string token) => token.Length > 0 && token[0] is >= '؀' and <= 'ۿ';

    private sealed record WordSpec(string Word, bool Prefix)
    {
        public bool Hit(string token) => Prefix ? token.StartsWith(Word, StringComparison.Ordinal) : token == Word;
    }

    private sealed record Alternative(IReadOnlyList<WordSpec> Words, bool Arabic)
    {
        /// <summary>Number of tokens consumed at <paramref name="position"/>, or -1. Clitics are allowed on the first word only.</summary>
        public int MatchLength(IReadOnlyList<string> tokens, int position)
        {
            if (position + Words.Count > tokens.Count || !FirstMatches(tokens[position]))
            {
                return -1;
            }

            for (var i = 1; i < Words.Count; i++)
            {
                if (!Words[i].Hit(tokens[position + i]))
                {
                    return -1;
                }
            }

            return Words.Count;
        }

        private bool FirstMatches(string token)
        {
            var first = Words[0];
            if (first.Hit(token))
            {
                return true;
            }

            if (!Arabic)
            {
                return false;
            }

            foreach (var clitic in ArabicClitics)
            {
                if (token.Length > clitic.Length && token.StartsWith(clitic, StringComparison.Ordinal) && first.Hit(token[clitic.Length..]))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private sealed record Element(IReadOnlyList<Alternative> Alternatives, bool Optional, bool IsGap)
    {
        public static readonly Element Gap = new(Array.Empty<Alternative>(), Optional: true, IsGap: true);

        public IEnumerable<int> MatchLengths(IReadOnlyList<string> tokens, int position) =>
            Alternatives.Select(a => a.MatchLength(tokens, position)).Where(n => n > 0).Distinct().OrderByDescending(n => n);
    }
}
