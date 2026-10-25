using Tamaneen.Crisis.Gate;
using Tamaneen.Crisis.Rules;

namespace Tamaneen.Crisis.Worker;

/// <summary>Maps rule ids stored on an event back to their clinical category, for the console's labels.</summary>
public sealed class RuleCategoryIndex
{
    private readonly Dictionary<string, string> _categories;

    public RuleCategoryIndex(IEnumerable<RulePack> packs)
    {
        _categories = packs.SelectMany(p => p.Rules).ToDictionary(r => r.Id, r => r.Category, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> CategoriesOf(string ruleIds) =>
        ruleIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => id == CrisisGate.SystemFailureRuleId ? "system"
                : id == CrisisGate.StaffManualRuleId ? "staff_alert"
                : id.StartsWith("Q-", StringComparison.Ordinal) ? "questionnaire"
                : _categories.GetValueOrDefault(id, "suicidal_ideation"))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
