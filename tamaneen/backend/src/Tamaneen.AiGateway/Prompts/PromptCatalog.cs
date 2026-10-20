using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tamaneen.SharedKernel;

namespace Tamaneen.AiGateway.Prompts;

public sealed record PromptLanguage(
    [property: JsonPropertyName("rule")] string Rule,
    [property: JsonPropertyName("repair")] string Repair,
    [property: JsonPropertyName("fallback")] string Fallback);

/// <summary>A versioned prompt. Versions are recorded with every call so a reply can be traced to its wording.</summary>
public sealed record PromptTemplate(
    [property: JsonPropertyName("purpose")] string Purpose,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("maxTokens")] int MaxTokens,
    [property: JsonPropertyName("maxReplyChars")] int MaxReplyChars,
    [property: JsonPropertyName("effort")] string Effort,
    [property: JsonPropertyName("historyTurns")] int HistoryTurns,
    [property: JsonPropertyName("system")] string System,
    [property: JsonPropertyName("languages")] IReadOnlyDictionary<string, PromptLanguage> Languages)
{
    public PromptLanguage For(string language) => Languages[SupportedLanguages.Normalize(language)];

    public string SystemFor(string language) => System.Replace("{language_rule}", For(language).Rule, StringComparison.Ordinal);
}

/// <summary>Prompt templates shipped inside the assembly; they change only through code review and a release.</summary>
public sealed class PromptCatalog
{
    private static readonly string[] Efforts = ["low", "medium", "high"];
    private readonly FrozenDictionary<string, PromptTemplate> templates;

    private PromptCatalog(FrozenDictionary<string, PromptTemplate> templates) => this.templates = templates;

    public IReadOnlyCollection<string> Purposes => templates.Keys;

    public static PromptCatalog LoadEmbedded()
    {
        var assembly = typeof(PromptCatalog).Assembly;
        var list = assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("prompts.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal))
            .Select(n => Read(assembly, n))
            .ToList();
        foreach (var t in list)
        {
            Validate(t);
        }

        return new PromptCatalog(list.ToFrozenDictionary(t => t.Purpose, StringComparer.Ordinal));
    }

    public PromptTemplate Get(string purpose) =>
        templates.TryGetValue(purpose, out var t) ? t : throw new KeyNotFoundException($"Unknown AI purpose '{purpose}'.");

    public bool Contains(string purpose) => templates.ContainsKey(purpose);

    internal static void Validate(PromptTemplate t)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(t.Purpose) || !t.Version.StartsWith(t.Purpose + "-", StringComparison.Ordinal))
        {
            problems.Add("version must start with '<purpose>-'");
        }

        if (!t.System.Contains("{language_rule}", StringComparison.Ordinal))
        {
            problems.Add("system prompt must contain {language_rule}");
        }

        if (t.MaxTokens is < 64 or > 4096 || t.MaxReplyChars < 200 || t.HistoryTurns is < 0 or > 40 || !Efforts.Contains(t.Effort))
        {
            problems.Add("limits out of range");
        }

        foreach (var lang in SupportedLanguages.All)
        {
            if (!t.Languages.TryGetValue(lang, out var l) || string.IsNullOrWhiteSpace(l.Rule) || string.IsNullOrWhiteSpace(l.Repair) || string.IsNullOrWhiteSpace(l.Fallback))
            {
                problems.Add($"language '{lang}' is incomplete");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException($"Prompt '{t.Purpose}' is invalid: {string.Join("; ", problems)}.");
        }
    }

    private static PromptTemplate Read(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException(name);
        return JsonSerializer.Deserialize<PromptTemplate>(stream) ?? throw new InvalidOperationException($"Empty prompt {name}.");
    }
}
