using System.Text.RegularExpressions;
using Tamaneen.AiGateway.Prompts;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests.Ai;

public class PromptCatalogTests
{
    [Fact]
    public void Every_prompt_is_versioned_and_complete_in_both_languages()
    {
        var catalog = PromptCatalog.LoadEmbedded();

        catalog.Purposes.Should().Contain(["companion", "journal-reflection"]);
        foreach (var purpose in catalog.Purposes)
        {
            var template = catalog.Get(purpose);
            template.Version.Should().MatchRegex($"^{purpose}-\\d{{4}}\\.\\d{{2}}\\.\\d+$");
            foreach (var language in SupportedLanguages.All)
            {
                template.SystemFor(language).Should().NotContain("{language_rule}").And.Contain(template.For(language).Rule);
            }

            template.For("ar").Fallback.Should().MatchRegex("^[\\p{IsArabic}\\s\\p{P}]+$");
            template.For("en").Fallback.Should().MatchRegex("^[A-Za-z\\s\\p{P}]+$");
        }
    }

    [Fact]
    public void A_template_missing_a_language_is_refused()
    {
        var broken = PromptCatalog.LoadEmbedded().Get("companion") with { Languages = new Dictionary<string, PromptLanguage> { ["en"] = new("r", "r", "f") } };

        var act = () => PromptCatalog.Validate(broken);

        act.Should().Throw<InvalidOperationException>().WithMessage("*'ar' is incomplete*");
    }
}

/// <summary>Only the AI gateway may talk to a language model, so de-identification and validation cannot be skipped.</summary>
public class AiBoundaryTests
{
    private static readonly string BackendRoot = FindBackendRoot();

    [Fact]
    public void Only_the_gateway_project_references_the_model_sdk()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(BackendRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !p.Contains("Tamaneen.AiGateway", StringComparison.Ordinal))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"Include=""(?:Anthropic|OpenAI|Azure\.AI\.OpenAI|Microsoft\.SemanticKernel)[^""]*"""))
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void No_code_outside_the_gateway_uses_the_model_sdk_or_the_raw_model()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(BackendRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}Tamaneen.AiGateway{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                        && !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"using\s+Anthropic|\bILanguageModel\b|api\.anthropic\.com"))
            .ToList();

        offenders.Should().BeEmpty();
    }

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src", "Tamaneen.AiGateway")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("backend root");
    }
}
