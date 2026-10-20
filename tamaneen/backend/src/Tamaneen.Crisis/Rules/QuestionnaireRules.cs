using System.Text.Json;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Rules;

/// <summary>
/// Item and total-score thresholds for screening instruments (PHQ-9, GAD-7, C-SSRS screen).
/// Answers are scored by code, never by the AI. Thresholds are data, versioned with the rule packs and,
/// later, edited by the clinical lead in the test builder (S46) as safety-linked items.
/// </summary>
public sealed class QuestionnaireRules
{
    private readonly Dictionary<string, InstrumentRules> _instruments;

    private QuestionnaireRules(string version, Dictionary<string, InstrumentRules> instruments)
    {
        Version = version;
        _instruments = instruments;
    }

    public string Version { get; }

    public IReadOnlyCollection<string> Instruments => _instruments.Keys;

    public static QuestionnaireRules Load(Stream json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, RulePack.JsonOptions) ?? throw new InvalidDataException("Questionnaire rules are empty.");
        if (string.IsNullOrWhiteSpace(dto.Version) || dto.Instruments is not { Count: > 0 })
        {
            throw new InvalidDataException("Questionnaire rules need a version and instruments.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var instruments = new Dictionary<string, InstrumentRules>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, inst) in dto.Instruments)
        {
            if (inst.ItemCount <= 0 || inst.MaxItemScore <= 0)
            {
                throw new InvalidDataException($"{code}: itemCount and maxItemScore are required.");
            }

            foreach (var rule in (inst.ItemRules ?? new()).Cast<RuleDto>().Concat(inst.TotalRules ?? new()))
            {
                if (string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) || rule.Level is RiskLevel.None)
                {
                    throw new InvalidDataException($"{code}: rule '{rule.Id}' needs a unique id and a level.");
                }
            }

            foreach (var item in inst.ItemRules ?? new())
            {
                if (item.Item < 1 || item.Item > inst.ItemCount)
                {
                    throw new InvalidDataException($"{code}: rule {item.Id} refers to item {item.Item}, outside 1..{inst.ItemCount}.");
                }
            }

            instruments[code] = new InstrumentRules(inst.ItemCount, inst.MaxItemScore, inst.ItemRules ?? new(), inst.TotalRules ?? new(), inst.CompleteWithoutRisk);
        }

        return new QuestionnaireRules(dto.Version!, instruments);
    }

    /// <summary>Evaluates a complete or partial set of answers. Every triggered rule is returned; the caller takes the highest.</summary>
    public IReadOnlyList<RuleHit> Evaluate(QuestionnaireAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (!_instruments.TryGetValue(answers.Instrument, out var inst))
        {
            throw new ArgumentException($"Unknown instrument '{answers.Instrument}'.", nameof(answers));
        }

        foreach (var (item, score) in answers.Scores)
        {
            if (item < 1 || item > inst.ItemCount || score < 0 || score > inst.MaxItemScore)
            {
                throw new ArgumentOutOfRangeException(nameof(answers), $"{answers.Instrument} item {item} = {score} is out of range.");
            }
        }

        var hits = new List<RuleHit>();
        foreach (var rule in inst.ItemRules)
        {
            if (answers.Scores.TryGetValue(rule.Item, out var score) && score >= rule.MinScore)
            {
                hits.Add(new RuleHit(rule.Id!, rule.Level, rule.Category ?? "questionnaire_item", RuleSource.Questionnaire));
            }
        }

        var complete = answers.Scores.Count == inst.ItemCount;
        if (complete)
        {
            var total = answers.Scores.Values.Sum();
            foreach (var rule in inst.TotalRules.Where(r => total >= r.MinTotal))
            {
                hits.Add(new RuleHit(rule.Id!, rule.Level, rule.Category ?? "questionnaire_total", RuleSource.Questionnaire));
            }

            if (hits.Count == 0 && inst.CompleteWithoutRisk is { } routine)
            {
                hits.Add(new RuleHit(routine, RiskLevel.Routine, "questionnaire_routine", RuleSource.Questionnaire));
            }
        }

        return hits;
    }

    private sealed record InstrumentRules(int ItemCount, int MaxItemScore, List<ItemRuleDto> ItemRules, List<TotalRuleDto> TotalRules, string? CompleteWithoutRisk);

    private sealed class Dto
    {
        public string? Version { get; set; }

        public Dictionary<string, InstrumentDto>? Instruments { get; set; }
    }

    private sealed class InstrumentDto
    {
        public int ItemCount { get; set; }

        public int MaxItemScore { get; set; }

        public List<ItemRuleDto>? ItemRules { get; set; }

        public List<TotalRuleDto>? TotalRules { get; set; }

        /// <summary>Rule id recorded when a complete instrument triggers nothing (Routine, level 1).</summary>
        public string? CompleteWithoutRisk { get; set; }
    }

    private class RuleDto
    {
        public string? Id { get; set; }

        public RiskLevel Level { get; set; }

        public string? Category { get; set; }
    }

    private sealed class ItemRuleDto : RuleDto
    {
        public int Item { get; set; }

        public int MinScore { get; set; }
    }

    private sealed class TotalRuleDto : RuleDto
    {
        public int MinTotal { get; set; }
    }
}

/// <summary>Scores by item number (1-based) for one instrument, e.g. PHQ-9 { 9: 1 }.</summary>
public sealed record QuestionnaireAnswers(string Instrument, IReadOnlyDictionary<int, int> Scores);

public enum RuleSource
{
    Text = 0,
    Questionnaire = 1,
    System = 2,
}

public sealed record RuleHit(string RuleId, RiskLevel Level, string Category, RuleSource Source);
