using System.Text;
using Tamaneen.Crisis.Rules;
using Tamaneen.Crisis.Text;

namespace Tamaneen.UnitTests.Crisis;

public class CrisisTextNormalizerTests
{
    [Theory]
    [InlineData("أَبِي أَمُوتُ", "ابي اموت")]
    [InlineData("إنتحار", "انتحار")]
    [InlineData("آخر رسالة", "اخر رساله")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("ابيييييي امووووت", "ابي اموت")]
    [InlineData("انـــتـــحـــار", "انتحار")]
    [InlineData("عبء", "عب")]
    [InlineData("ﻻ", "لا")]
    [InlineData("٩١١", "911")]
    [InlineData("I'm GONNA", "i m gona")]
    [InlineData("ante7er", "anteher")]
    [InlineData("a5les", "akhles")]
    [InlineData("3ib2", "aib")]
    [InlineData("call 911!", "cal 911")]
    [InlineData("  ...!!!  ", "")]
    public void Normalizes_spelling_variants_to_one_form(string input, string expected) =>
        CrisisTextNormalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public void Persian_letter_forms_fold_to_arabic()
    {
        CrisisTextNormalizer.Normalize("کی").Should().Be("كي");
    }

    [Fact]
    public void Survives_very_long_input_quickly()
    {
        var text = new StringBuilder().Insert(0, "ابي اموت ", 20_000).ToString();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        CrisisTextNormalizer.Tokenize(text).Should().HaveCount(40_000);
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
}

public class PhrasePatternTests
{
    private static bool Hits(string pattern, string text) => PhrasePattern.Parse(pattern).Match(CrisisTextNormalizer.Tokenize(text)) is not null;

    [Theory]
    [InlineData("ابي اموت", "والله ابي اموت", true)]
    [InlineData("ابي اموت", "وابي واموت", true)]
    [InlineData("اموت", "ساموت", true)]
    [InlineData("اموت", "الموتى", false)]
    [InlineData("انتحر*", "انتحرت", true)]
    [InlineData("ابي|ودي اموت", "ودي اموت", true)]
    [InlineData("ابي to? die", "ابي die", true)]
    [InlineData("want to? die", "want die", true)]
    [InlineData("want to? die", "want to die", true)]
    [InlineData("want ... die", "want to really die", true)]
    [InlineData("want ... die", "want to really really really really die", false)]
    [InlineData("ما_في سبب", "ما في سبب", true)]
    [InlineData("ما_في سبب", "ما سبب", false)]
    [InlineData("kill myself", "skill myself", false)]
    [InlineData("die", "dies", false)]
    public void Matches_words_in_order_with_clitics_alternatives_and_gaps(string pattern, string text, bool expected) =>
        Hits(pattern, text).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("... ابي")]
    [InlineData("ابي ...")]
    [InlineData("a? b?")]
    [InlineData("hello-world")]
    public void Rejects_malformed_patterns(string pattern)
    {
        var act = () => PhrasePattern.Parse(pattern);
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void Every_match_is_reported_so_an_excused_one_cannot_hide_another()
    {
        var spans = PhrasePattern.Parse("ابي اموت").Matches(CrisisTextNormalizer.Tokenize("ابي اموت من الضحك بس ابي اموت")).ToList();
        spans.Should().HaveCount(2);
    }
}
