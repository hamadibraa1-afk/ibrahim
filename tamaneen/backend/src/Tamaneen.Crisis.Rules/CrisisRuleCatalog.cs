using System.Reflection;
using Tamaneen.Crisis.Assessment;

namespace Tamaneen.Crisis.Rules;

/// <summary>Loads and validates the packs shipped in this assembly. A pack that fails validation stops the process from starting.</summary>
public static class CrisisRuleCatalog
{
    public static readonly string[] TextPacks = { "ar", "en" };

    public static IReadOnlyList<RulePack> LoadTextPacks() => TextPacks.Select(name => RulePack.Load(Open($"{name}.json"))).ToList();

    public static QuestionnaireRules LoadQuestionnaires() => QuestionnaireRules.Load(Open("questionnaires.json"));

    public static CrisisScripts LoadScripts() => CrisisScripts.Load(Open("scripts.json"));

    public static RiskEvaluator CreateEvaluator() => new(LoadTextPacks(), LoadQuestionnaires());

    /// <summary>Labelled evaluation cases (text → expected level) used as the release gate.</summary>
    public static Stream OpenEvaluationSet() => Open("evaluation-set.json");

    private static Stream Open(string file) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"packs.{file}")
        ?? throw new FileNotFoundException($"Embedded rule pack '{file}' is missing.");
}
