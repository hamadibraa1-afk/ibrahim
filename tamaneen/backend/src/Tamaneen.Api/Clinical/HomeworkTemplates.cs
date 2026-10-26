using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tamaneen.Api.Clinical;

/// <summary>The clinician-approved homework library (S16, SVC-59), bilingual. The therapist may also write their own.</summary>
public sealed class HomeworkTemplates
{
    private HomeworkTemplates(string version, IReadOnlyList<Template> items)
    {
        Version = version;
        Items = items;
    }

    public string Version { get; }

    public IReadOnlyList<Template> Items { get; }

    public static HomeworkTemplates LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("homework-templates.json") ?? throw new FileNotFoundException("homework-templates.json");
        var file = JsonSerializer.Deserialize<FileDto>(stream) ?? throw new InvalidOperationException("Empty homework-templates.json");
        if (file.Templates.Any(t => string.IsNullOrWhiteSpace(t.Title.Ar) || string.IsNullOrWhiteSpace(t.Title.En) || t.Instructions.Ar.Count == 0 || t.Instructions.En.Count == 0))
        {
            throw new InvalidOperationException("A homework template needs Arabic and English text.");
        }

        return new HomeworkTemplates(file.Version, file.Templates);
    }

    public bool Contains(string code) => Items.Any(t => t.Code == code);

    public sealed record Text([property: JsonPropertyName("ar")] string Ar, [property: JsonPropertyName("en")] string En);

    public sealed record Lines([property: JsonPropertyName("ar")] List<string> Ar, [property: JsonPropertyName("en")] List<string> En);

    public sealed record Template(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("frequency")] string Frequency,
        [property: JsonPropertyName("minutes")] int? Minutes,
        [property: JsonPropertyName("title")] Text Title,
        [property: JsonPropertyName("instructions")] Lines Instructions);

    private sealed record FileDto([property: JsonPropertyName("version")] string Version, [property: JsonPropertyName("templates")] List<Template> Templates);
}
