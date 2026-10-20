using Tamaneen.Crisis.Rules;
using Tamaneen.Crisis.Text;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Assessment;

/// <summary>
/// Deterministic risk assessment. Every text is checked against ALL rule packs (Arabic, Gulf dialect,
/// Arabizi and English), whatever language the patient picked, because people mix languages when distressed.
/// The highest level triggered wins.
/// </summary>
public sealed class RiskEvaluator : IRiskEvaluator
{
    private readonly IReadOnlyList<RulePack> _packs;
    private readonly QuestionnaireRules _questionnaires;

    public RiskEvaluator(IReadOnlyList<RulePack> packs, QuestionnaireRules questionnaires)
    {
        if (packs is not { Count: > 0 })
        {
            throw new ArgumentException("At least one rule pack is required.", nameof(packs));
        }

        _packs = packs;
        _questionnaires = questionnaires ?? throw new ArgumentNullException(nameof(questionnaires));
        Version = string.Join('+', packs.Select(p => p.Version).Append(questionnaires.Version));
    }

    public string Version { get; }

    public RiskAssessment Assess(string? text, QuestionnaireAnswers? answers = null)
    {
        var hits = new List<RuleHit>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var tokens = CrisisTextNormalizer.Tokenize(text);
            foreach (var pack in _packs)
            {
                foreach (var rule in pack.Rules)
                {
                    if (rule.IsTriggeredBy(tokens))
                    {
                        hits.Add(new RuleHit(rule.Id, rule.Level, rule.Category, RuleSource.Text));
                    }
                }
            }
        }

        if (answers is not null)
        {
            hits.AddRange(_questionnaires.Evaluate(answers));
        }

        var level = hits.Count == 0 ? RiskLevel.None : hits.Max(h => h.Level);
        return new RiskAssessment(level, hits, Version);
    }
}

public interface IRiskEvaluator
{
    string Version { get; }

    RiskAssessment Assess(string? text, QuestionnaireAnswers? answers = null);
}

public sealed record RiskAssessment(RiskLevel Level, IReadOnlyList<RuleHit> Hits, string RulesVersion)
{
    public bool IsCrisis => Level >= RiskLevel.High;

    public IReadOnlyList<string> RuleIds => Hits.Select(h => h.RuleId).Distinct(StringComparer.Ordinal).ToList();
}
