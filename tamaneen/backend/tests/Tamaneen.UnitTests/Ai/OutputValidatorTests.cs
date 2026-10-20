using Microsoft.Extensions.Options;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Validation;

namespace Tamaneen.UnitTests.Ai;

public class OutputValidatorTests
{
    private static readonly OutputValidator Validator = new(Options.Create(new OutputValidatorOptions()));

    private static async Task<OutputValidation> Check(string reply, string language, PiiTokenMap? map = null, bool truncated = false) =>
        await Validator.ValidateAsync(reply, language, 1800, truncated, map ?? new PiiTokenMap(), CancellationToken.None);

    [Theory]
    [InlineData("يبدو أن اليوم كان ثقيلاً عليك، ومن الطبيعي أن تشعر بالتعب. هل تود أن نجرب تمرين تنفس قصير معاً؟", "ar")]
    [InlineData("It sounds like today was really heavy, and it makes sense that you feel drained. Would you like to try a short breathing exercise together?", "en")]
    [InlineData("أفهم أن تمرين CBT الذي جربته لم يناسبك، وهذا لا يعني أنك فشلت. ما الذي لاحظته أثناءه؟", "ar")]
    public async Task Supportive_replies_in_the_right_language_pass(string reply, string language)
    {
        (await Check(reply, language)).Passed.Should().BeTrue();
    }

    [Theory]
    [InlineData("It sounds like a hard day. Would you like to talk about it?", "ar")]
    [InlineData("يبدو أنه يوم صعب. هل تود الحديث عنه؟", "en")]
    [InlineData("Ana fahem shoo2ak, khalina njarreb nitnaffas", "ar")]
    public async Task Replies_in_the_wrong_language_are_flagged_alone(string reply, string language)
    {
        var result = await Check(reply, language);

        result.Violations.Should().Equal(OutputViolation.WrongLanguage);
        result.OnlyLanguage.Should().BeTrue();
    }

    [Theory]
    [InlineData("From what you describe, you have depression and should see someone.", "en", OutputViolation.Diagnosis)]
    [InlineData("You might have bipolar disorder.", "en", OutputViolation.Diagnosis)]
    [InlineData("من كلامك يبدو أنك مصاب بالاكتئاب السريري.", "ar", OutputViolation.Diagnosis)]
    [InlineData("أنت تعاني من اضطراب القلق العام.", "ar", OutputViolation.Diagnosis)]
    [InlineData("Maybe ask about sertraline, it helps many people.", "en", OutputViolation.Medication)]
    [InlineData("Taking 50 mg before bed may help you sleep.", "en", OutputViolation.Medication)]
    [InlineData("ممكن تسأل طبيبك عن السيبرالكس.", "ar", OutputViolation.Medication)]
    [InlineData("جرب حبوب النوم لليلة وحدة بس.", "ar", OutputViolation.Medication)]
    [InlineData("خذ ٢٥ ملغ قبل النوم.", "ar", OutputViolation.Medication)]
    [InlineData("A lethal dose would be dangerous.", "en", OutputViolation.HarmfulMethod)]
    [InlineData("There is no painless way, please talk to someone.", "en", OutputViolation.HarmfulMethod)]
    [InlineData("الجرعة الزائدة خطيرة جداً.", "ar", OutputViolation.HarmfulMethod)]
    [InlineData("Call the clinic on 0112345678 tomorrow.", "en", OutputViolation.ContactDetails)]
    [InlineData("Read more at https://example.com/help today.", "en", OutputViolation.ContactDetails)]
    [InlineData("اكتب لنا على help@example.com أي وقت.", "ar", OutputViolation.ContactDetails)]
    [InlineData("Tell <PERSON_9> how you feel.", "en", OutputViolation.UnknownPlaceholder)]
    public async Task Out_of_remit_content_is_rejected(string reply, string language, OutputViolation expected)
    {
        (await Check(reply, language)).Violations.Should().Contain(expected);
    }

    [Fact]
    public async Task Known_placeholders_are_allowed_and_raw_values_are_not()
    {
        var map = new PiiTokenMap();
        map.TokenFor("سارة", PiiType.Person);
        map.TokenFor("0551234567", PiiType.Phone);

        (await Check("من الجميل أنك تحدثت مع <PERSON_1> اليوم. كيف شعرت بعدها؟", "ar", map)).Passed.Should().BeTrue();
        (await Check("من الجميل أنك تحدثت مع سارة اليوم. كيف شعرت بعدها؟", "ar", map)).Violations.Should().Contain(OutputViolation.PersonalDataEcho);
    }

    [Fact]
    public async Task Empty_cut_off_and_overlong_replies_fail()
    {
        (await Check("   ", "en")).Violations.Should().Equal(OutputViolation.Empty);
        (await Check("That sounds hard and", "en", truncated: true)).Violations.Should().Contain(OutputViolation.Truncated);
        (await Check(string.Concat(Enumerable.Repeat("You are doing your best. ", 100)), "en")).Violations.Should().Contain(OutputViolation.TooLong);
    }
}
