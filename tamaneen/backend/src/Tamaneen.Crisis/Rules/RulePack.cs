using System.Text.Json;
using System.Text.Json.Serialization;
using Tamaneen.Crisis.Text;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Rules;

/// <summary>One versioned set of text rules for a language (Arabic includes Gulf dialect and Arabizi).</summary>
public sealed class RulePack
{
    private RulePack(string language, string version, IReadOnlyList<TextRule> rules)
    {
        Language = language;
        Version = version;
        Rules = rules;
    }

    public string Language { get; }

    public string Version { get; }

    public IReadOnlyList<TextRule> Rules { get; }

    /// <summary>
    /// Parses and validates a pack. A pack is refused unless every rule's examples trigger that rule and
    /// none of its counter-examples do: a broken rule can never reach production silently.
    /// </summary>
    public static RulePack Load(Stream json)
    {
        var dto = JsonSerializer.Deserialize<RulePackDto>(json, JsonOptions)
            ?? throw new InvalidDataException("Rule pack is empty.");
        if (string.IsNullOrWhiteSpace(dto.Version) || !SupportedLanguages.IsSupported(dto.Language))
        {
            throw new InvalidDataException("Rule pack needs a version and a language of ar or en.");
        }

        if (dto.Rules is not { Count: > 0 })
        {
            throw new InvalidDataException($"Rule pack {dto.Version} has no rules.");
        }

        var rules = new List<TextRule>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in dto.Rules)
        {
            if (string.IsNullOrWhiteSpace(r.Id) || !ids.Add(r.Id))
            {
                throw new InvalidDataException($"Rule id '{r.Id}' is missing or duplicated in {dto.Version}.");
            }

            if (r.Level is not (RiskLevel.High or RiskLevel.Critical))
            {
                throw new InvalidDataException($"Rule {r.Id}: text rules must be High or Critical.");
            }

            if (r.Patterns is not { Count: > 0 } || r.Examples is not { Count: > 0 })
            {
                throw new InvalidDataException($"Rule {r.Id} needs patterns and at least one example.");
            }

            var rule = new TextRule(
                r.Id,
                r.Level,
                r.Category ?? "unspecified",
                r.Patterns.Select(PhrasePattern.Parse).ToList(),
                (r.Exceptions ?? new List<string>()).Select(PhrasePattern.Parse).ToList());

            foreach (var example in r.Examples)
            {
                if (!rule.IsTriggeredBy(CrisisTextNormalizer.Tokenize(example)))
                {
                    throw new InvalidDataException($"Rule {r.Id}: example \"{example}\" does not trigger the rule.");
                }
            }

            foreach (var counter in r.CounterExamples ?? new List<string>())
            {
                if (rule.IsTriggeredBy(CrisisTextNormalizer.Tokenize(counter)))
                {
                    throw new InvalidDataException($"Rule {r.Id}: counter-example \"{counter}\" triggers the rule.");
                }
            }

            rules.Add(rule);
        }

        return new RulePack(SupportedLanguages.Normalize(dto.Language), dto.Version!, rules);
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed class RulePackDto
    {
        public string? Language { get; set; }

        public string? Version { get; set; }

        public List<RuleDto>? Rules { get; set; }
    }

    private sealed class RuleDto
    {
        public string? Id { get; set; }

        public RiskLevel Level { get; set; }

        public string? Category { get; set; }

        public List<string>? Patterns { get; set; }

        public List<string>? Exceptions { get; set; }

        public List<string>? Examples { get; set; }

        public List<string>? CounterExamples { get; set; }
    }
}

/// <summary>A clinician-owned rule: any pattern triggers it unless an exception covers the same words.</summary>
public sealed class TextRule
{
    public TextRule(string id, RiskLevel level, string category, IReadOnlyList<PhrasePattern> patterns, IReadOnlyList<PhrasePattern> exceptions)
    {
        Id = id;
        Level = level;
        Category = category;
        Patterns = patterns;
        Exceptions = exceptions;
    }

    public string Id { get; }

    public RiskLevel Level { get; }

    public string Category { get; }

    public IReadOnlyList<PhrasePattern> Patterns { get; }

    public IReadOnlyList<PhrasePattern> Exceptions { get; }

    public bool IsTriggeredBy(IReadOnlyList<string> tokens)
    {
        List<(int Start, int End)>? excuses = null;
        foreach (var pattern in Patterns)
        {
            foreach (var hit in pattern.Matches(tokens))
            {
                // An exception ("اموت ضحك", "dying to see you") excuses only a hit whose words it overlaps.
                excuses ??= Exceptions.SelectMany(e => e.Matches(tokens)).ToList();
                if (!excuses.Any(ex => ex.Start < hit.End && hit.Start < ex.End))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
