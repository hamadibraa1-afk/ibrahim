using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tamaneen.Api.Clinical;

/// <summary>Bilingual item texts for PHQ-9, GAD-7 and the C-SSRS screener (scoring lives in Crisis.Rules).</summary>
public sealed class QuestionnaireCatalog
{
    private readonly Dictionary<string, Instrument> _instruments;

    private QuestionnaireCatalog(string version, Dictionary<string, Instrument> instruments)
    {
        Version = version;
        _instruments = instruments;
    }

    public string Version { get; }

    public static QuestionnaireCatalog LoadEmbedded()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("questionnaires.json") ?? throw new FileNotFoundException("questionnaires.json");
        var file = JsonSerializer.Deserialize<FileDto>(stream) ?? throw new InvalidOperationException("Empty questionnaires.json");
        foreach (var (name, i) in file.Instruments)
        {
            if (i.Items.Count == 0 || i.Items.Any(t => string.IsNullOrWhiteSpace(t.Ar) || string.IsNullOrWhiteSpace(t.En)) || i.Options.Count == 0)
            {
                throw new InvalidOperationException($"Questionnaire {name} is incomplete.");
            }
        }

        return new QuestionnaireCatalog(file.Version, file.Instruments);
    }

    public int ItemCount(string instrument) => Get(instrument).Items.Count;

    public string ItemText(string instrument, int item, string language) => Pick(Get(instrument).Items[item - 1], language);

    public string Stem(string instrument, string language) => Pick(Get(instrument).Stem, language);

    public IReadOnlyList<(int Value, string Label)> Options(string instrument, string language) =>
        Get(instrument).Options.Select(o => (o.Value, language == "en" ? o.En : o.Ar)).ToList();

    private Instrument Get(string instrument) =>
        _instruments.TryGetValue(instrument, out var i) ? i : throw new KeyNotFoundException(instrument);

    private static string Pick(Text text, string language) => language == "en" ? text.En : text.Ar;

    private sealed record FileDto([property: JsonPropertyName("version")] string Version, [property: JsonPropertyName("instruments")] Dictionary<string, Instrument> Instruments);

    private sealed record Instrument([property: JsonPropertyName("stem")] Text Stem, [property: JsonPropertyName("options")] List<Option> Options, [property: JsonPropertyName("items")] List<Text> Items);

    private sealed record Text([property: JsonPropertyName("ar")] string Ar, [property: JsonPropertyName("en")] string En);

    private sealed record Option([property: JsonPropertyName("value")] int Value, [property: JsonPropertyName("ar")] string Ar, [property: JsonPropertyName("en")] string En);
}
