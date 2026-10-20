using System.Text;
using Tamaneen.Crisis.Assessment;
using Tamaneen.Crisis.Rules;
using Tamaneen.Crisis.Text;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests.Crisis;

public class RulePackTests
{
    private static MemoryStream Json(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void Shipped_packs_load_and_every_rule_has_examples_that_trigger_it()
    {
        var packs = CrisisRuleCatalog.LoadTextPacks();
        packs.Select(p => p.Language).Should().BeEquivalentTo(new[] { "ar", "en" });
        packs.SelectMany(p => p.Rules).Select(r => r.Id).Should().OnlyHaveUniqueItems();
        packs.SelectMany(p => p.Rules).Should().Contain(r => r.Category == "harm_to_others", "harm to others is a first-class risk type");
    }

    [Fact]
    public void A_rule_whose_example_does_not_trigger_it_is_refused()
    {
        var act = () => RulePack.Load(Json("""{"language":"ar","version":"t1","rules":[{"id":"X","level":"Critical","patterns":["ابي اموت"],"examples":["ابي انام"]}]}"""));
        act.Should().Throw<InvalidDataException>().WithMessage("*does not trigger*");
    }

    [Fact]
    public void A_rule_triggered_by_its_counter_example_is_refused()
    {
        var act = () => RulePack.Load(Json("""{"language":"ar","version":"t1","rules":[{"id":"X","level":"High","patterns":["اموت"],"examples":["ابي اموت"],"counterExamples":["اموت فيك"]}]}"""));
        act.Should().Throw<InvalidDataException>().WithMessage("*counter-example*");
    }

    [Theory]
    [InlineData("""{"language":"fr","version":"t","rules":[]}""")]
    [InlineData("""{"language":"ar","version":"","rules":[]}""")]
    [InlineData("""{"language":"ar","version":"t","rules":[{"id":"X","level":"Elevated","patterns":["a"],"examples":["a"]}]}""")]
    [InlineData("""{"language":"ar","version":"t","rules":[{"id":"X","level":"High","patterns":["a"],"examples":["a"]},{"id":"X","level":"High","patterns":["b"],"examples":["b"]}]}""")]
    public void Invalid_packs_are_refused(string json)
    {
        var act = () => RulePack.Load(Json(json));
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Scripts_exist_in_both_languages_and_show_the_emergency_number()
    {
        var scripts = CrisisRuleCatalog.LoadScripts();
        foreach (var kind in Enum.GetValues<CrisisScriptKind>())
        {
            scripts.Render(kind, "ar", "911", "920033360").Should().Contain("911").And.MatchRegex("[؀-ۿ]");
            scripts.Render(kind, "en", "911", "920033360").Should().Contain("911").And.NotMatchRegex("[؀-ۿ]");
        }

        scripts.Render(CrisisScriptKind.Critical, "ar", "911", "1").Should().StartWith("أنت لست وحدك");
        scripts.Render(CrisisScriptKind.Critical, "en", "911", "1").Should().StartWith("You are not alone");
    }

    [Fact]
    public void A_script_missing_a_language_is_refused()
    {
        var act = () => CrisisScripts.Load(Json("""{"version":"s","scripts":{"High":{"ar":"اتصل {emergency_number}"},"Critical":{"ar":"x {emergency_number}","en":"x {emergency_number}"},"SystemFailure":{"ar":"x {emergency_number}","en":"x {emergency_number}"}}}"""));
        act.Should().Throw<InvalidDataException>().WithMessage("*High*en*");
    }
}

public class QuestionnaireRulesTests
{
    private readonly RiskEvaluator _evaluator = CrisisRuleCatalog.CreateEvaluator();

    private static QuestionnaireAnswers Phq9(params int[] scores) =>
        new("PHQ-9", scores.Select((s, i) => (i + 1, s)).ToDictionary(x => x.Item1, x => x.s));

    [Fact]
    public void Phq9_item_9_above_zero_is_high_even_with_a_low_total()
    {
        _evaluator.Assess(null, Phq9(0, 0, 0, 0, 0, 0, 0, 0, 1)).Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Phq9_item_9_counts_as_soon_as_it_is_answered()
    {
        var partial = new QuestionnaireAnswers("PHQ-9", new Dictionary<int, int> { [9] = 2 });
        _evaluator.Assess(null, partial).Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Phq9_total_of_15_without_item_9_is_elevated()
    {
        var a = _evaluator.Assess(null, Phq9(2, 2, 2, 2, 2, 2, 2, 1, 0));
        a.Level.Should().Be(RiskLevel.Elevated);
        a.RuleIds.Should().Equal("Q-PHQ9-T15");
    }

    [Fact]
    public void Complete_low_scores_are_routine_and_partial_answers_are_none()
    {
        _evaluator.Assess(null, Phq9(1, 1, 1, 0, 0, 0, 0, 0, 0)).Level.Should().Be(RiskLevel.Routine);
        _evaluator.Assess(null, new QuestionnaireAnswers("PHQ-9", new Dictionary<int, int> { [1] = 1 })).Level.Should().Be(RiskLevel.None);
    }

    [Theory]
    [InlineData(1, RiskLevel.High)]
    [InlineData(2, RiskLevel.High)]
    [InlineData(3, RiskLevel.High)]
    [InlineData(4, RiskLevel.Critical)]
    [InlineData(5, RiskLevel.Critical)]
    [InlineData(6, RiskLevel.Critical)]
    public void Cssrs_items_map_to_their_levels(int item, RiskLevel expected)
    {
        var answers = new QuestionnaireAnswers("C-SSRS-SCREEN", new Dictionary<int, int> { [item] = 1 });
        _evaluator.Assess(null, answers).Level.Should().Be(expected);
    }

    [Fact]
    public void Text_and_answers_combine_and_the_highest_wins()
    {
        var a = _evaluator.Assess("ابي اموت", new QuestionnaireAnswers("C-SSRS-SCREEN", new Dictionary<int, int> { [5] = 1 }));
        a.Level.Should().Be(RiskLevel.Critical);
        a.RuleIds.Should().Contain(new[] { "AR-H-IDEATION", "Q-CSSRS-5" });
    }

    [Fact]
    public void Out_of_range_answers_are_rejected_not_ignored()
    {
        var act = () => _evaluator.Assess(null, new QuestionnaireAnswers("PHQ-9", new Dictionary<int, int> { [9] = 7 }));
        act.Should().Throw<ArgumentOutOfRangeException>();
        var unknown = () => _evaluator.Assess(null, new QuestionnaireAnswers("XYZ", new Dictionary<int, int>()));
        unknown.Should().Throw<ArgumentException>();
    }
}

public class TextAssessmentTests
{
    private readonly RiskEvaluator _evaluator = CrisisRuleCatalog.CreateEvaluator();

    [Theory]
    [InlineData("ابي اموت", RiskLevel.High)]
    [InlineData("سأنتحر الليلة", RiskLevel.Critical)]
    [InlineData("abi amoot", RiskLevel.High)]
    [InlineData("I want to kill myself", RiskLevel.Critical)]
    [InlineData("أموت فيك", RiskLevel.None)]
    [InlineData("I'm dying to see you", RiskLevel.None)]
    [InlineData("", RiskLevel.None)]
    [InlineData(null, RiskLevel.None)]
    public void Assesses_text_in_any_language(string? text, RiskLevel expected) =>
        _evaluator.Assess(text).Level.Should().Be(expected);

    [Fact]
    public void Every_pack_is_checked_whatever_language_the_patient_chose()
    {
        // An Arabic-speaking patient writing in English (or the reverse) is still caught.
        _evaluator.Assess("اليوم كان صعب and I want to die").Level.Should().Be(RiskLevel.High);
        _evaluator.Assess("honestly bante7er").Level.Should().Be(RiskLevel.Critical);
    }

    [Fact]
    public void An_idiom_does_not_hide_a_real_statement_later_in_the_same_message()
    {
        _evaluator.Assess("أموت من الضحك بس بصراحة أبي أموت").Level.Should().Be(RiskLevel.High);
    }

    [Fact]
    public void Saying_it_was_a_joke_does_not_lower_the_level()
    {
        _evaluator.Assess("سأنتحر الليلة. أمزح").Level.Should().Be(RiskLevel.Critical);
    }
}
