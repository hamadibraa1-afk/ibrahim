using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public class SupportedLanguagesTests
{
    [Theory]
    [InlineData("ar", "ar")]
    [InlineData("ar-SA", "ar")]
    [InlineData("AR", "ar")]
    [InlineData("en", "en")]
    [InlineData("en_US", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("fr", "ar")]
    [InlineData("", "ar")]
    [InlineData(null, "ar")]
    public void Normalize_maps_variants_and_falls_back_to_arabic(string? input, string expected) =>
        SupportedLanguages.Normalize(input).Should().Be(expected);

    [Fact]
    public void Arabic_is_right_to_left_and_english_left_to_right()
    {
        SupportedLanguages.Direction("ar").Should().Be("rtl");
        SupportedLanguages.Direction("ar-EG").Should().Be("rtl");
        SupportedLanguages.Direction("en").Should().Be("ltr");
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("en-US", true)]
    [InlineData("fr", false)]
    [InlineData(null, false)]
    public void IsSupported_accepts_only_arabic_and_english(string? code, bool expected) =>
        SupportedLanguages.IsSupported(code).Should().Be(expected);
}

public class PseudonymIdTests
{
    [Fact]
    public void New_ids_have_the_documented_format_and_no_ambiguous_letters()
    {
        for (var i = 0; i < 500; i++)
        {
            var id = PseudonymId.New().Value;
            id.Should().MatchRegex("^P-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{4}$");
            id.Should().NotContainAny("I", "L", "O", "U");
        }
    }

    [Fact]
    public void New_ids_do_not_collide_in_a_large_sample()
    {
        var ids = Enumerable.Range(0, 20_000).Select(_ => PseudonymId.New().Value).ToHashSet();
        ids.Should().HaveCount(20_000);
    }

    [Theory]
    [InlineData("p-ab12-cd34", "P-AB12-CD34")]
    [InlineData(" P-0000-ZZZZ ", "P-0000-ZZZZ")]
    public void Parse_normalizes_case_and_whitespace(string input, string expected) =>
        PseudonymId.Parse(input).Value.Should().Be(expected);

    [Theory]
    [InlineData("P-AB1-CD34")]
    [InlineData("P-ABIL-CD34")]
    [InlineData("X-AB12-CD34")]
    [InlineData("")]
    public void Parse_rejects_invalid_values(string input)
    {
        var act = () => PseudonymId.Parse(input);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.InvalidPseudonym);
    }

    [Fact]
    public void Roles_split_people_from_services()
    {
        AppRoles.IsService(AppRoles.CrisisService).Should().BeTrue();
        AppRoles.IsService(AppRoles.DataProtectionService).Should().BeTrue();
        AppRoles.IsService(AppRoles.Therapist).Should().BeFalse();
        AppRoles.Clinical.Should().NotContain(AppRoles.Admin);
    }
}
