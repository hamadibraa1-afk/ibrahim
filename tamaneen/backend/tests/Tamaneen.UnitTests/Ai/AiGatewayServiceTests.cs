using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tamaneen.AiGateway;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.AiGateway.Gateway;
using Tamaneen.AiGateway.Model;
using Tamaneen.AiGateway.Prompts;
using Tamaneen.AiGateway.Validation;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests.Ai;

public class AiGatewayServiceTests
{
    private const string Pseudonym = "P-AB12-CD34";
    private static readonly PromptCatalog Prompts = PromptCatalog.LoadEmbedded();

    [Fact]
    public async Task Arabic_conversation_reaches_the_model_without_personal_data_and_comes_back_in_arabic()
    {
        var fixture = new Fixture();
        fixture.Model.Reply("يبدو أن الخلاف مع <PERSON_1> أثقل عليك. من حقك أن تشعر بالضيق. هل تود أن نجرب تمرين تنفس قصير؟");

        var response = await fixture.Gateway.AskAsync(Ask("ar", "تضاربت مع أختي سارة ورقمها 0551234567 ما ترد عليه"), CancellationToken.None);

        var sent = fixture.Model.Requests.Single();
        sent.Messages.Single().Text.Should().NotContain("سارة").And.NotContain("0551234567").And.Contain("<PERSON_1>").And.Contain("<PHONE_1>");
        sent.System.Should().Contain("Always reply in Arabic only").And.NotContain("{language_rule}");
        response.Should().BeEquivalentTo(new { Language = "ar", IsFallback = false, Outcome = AiCallOutcome.Succeeded });
        response.Text.Should().Contain("سارة").And.NotContain("<PERSON_1>");
        fixture.Log.Records.Single().Should().BeEquivalentTo(new { PseudonymId = Pseudonym, Purpose = "companion", Language = "ar", Outcome = AiCallOutcome.Succeeded, InputTokens = 120, OutputTokens = 40 });
    }

    [Fact]
    public async Task English_conversation_is_instructed_and_answered_in_english()
    {
        var fixture = new Fixture();
        fixture.Model.Reply("That sounds exhausting, and it makes sense that you feel stuck. Would it help to name one small thing for tomorrow?");

        var response = await fixture.Gateway.AskAsync(Ask("en", "My name is John Carter and work is crushing me"), CancellationToken.None);

        fixture.Model.Requests.Single().System.Should().Contain("Always reply in English only");
        fixture.Model.Requests.Single().Messages.Single().Text.Should().NotContain("John").And.NotContain("Carter");
        response.Should().BeEquivalentTo(new { Language = "en", IsFallback = false, Outcome = AiCallOutcome.Succeeded });
    }

    [Fact]
    public async Task A_reply_in_the_wrong_language_is_rewritten_once()
    {
        var fixture = new Fixture();
        fixture.Model.Reply("That sounds really hard. Do you want to talk about it?");
        fixture.Model.Reply("يبدو هذا صعباً حقاً. هل تود أن نتحدث عنه؟");

        var response = await fixture.Gateway.AskAsync(Ask("ar", "اليوم كان صعب"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { Text = "يبدو هذا صعباً حقاً. هل تود أن نتحدث عنه؟", IsFallback = false, Outcome = AiCallOutcome.LanguageRepaired });
        var repair = fixture.Model.Requests[1].Messages;
        repair.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User);
        repair[^1].Text.Should().Be(Prompts.Get("companion").For("ar").Repair);
    }

    [Theory]
    [InlineData("ar")]
    [InlineData("en")]
    public async Task A_reply_that_stays_in_the_wrong_language_becomes_the_localized_fallback(string language)
    {
        var fixture = new Fixture();
        var wrong = language == "ar" ? "Still English, sorry about that." : "ما زلت أكتب بالعربية للأسف.";
        fixture.Model.Reply(wrong);
        fixture.Model.Reply(wrong);

        var response = await fixture.Gateway.AskAsync(Ask(language, language == "ar" ? "تعبان" : "tired"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { Text = Prompts.Get("companion").For(language).Fallback, Language = language, IsFallback = true, Outcome = AiCallOutcome.ValidatorRejected });
        fixture.Model.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("en", "You clearly have depression. Ask your doctor about sertraline 50 mg.")]
    [InlineData("ar", "يبدو أنك مصاب بالاكتئاب، اسأل عن السيبرالكس.")]
    public async Task Diagnosis_or_medication_never_reaches_the_patient(string language, string reply)
    {
        var fixture = new Fixture();
        fixture.Model.Reply(reply);

        var response = await fixture.Gateway.AskAsync(Ask(language, language == "ar" ? "ما أنام" : "I can't sleep"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { IsFallback = true, Outcome = AiCallOutcome.ValidatorRejected });
        fixture.Model.Requests.Should().ContainSingle("only language problems get a rewrite");
    }

    [Fact]
    public async Task A_refusal_becomes_the_fallback()
    {
        var fixture = new Fixture();
        fixture.Model.Reply(string.Empty, LanguageModelStop.Refused);

        var response = await fixture.Gateway.AskAsync(Ask("en", "hello"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { IsFallback = true, Outcome = AiCallOutcome.Refused });
    }

    [Fact]
    public async Task One_transient_failure_is_retried()
    {
        var fixture = new Fixture();
        fixture.Model.Fail(new TransientModelException("overloaded"));
        fixture.Model.Reply("I'm glad you reached out. What feels heaviest right now?");

        var response = await fixture.Gateway.AskAsync(Ask("en", "hello"), CancellationToken.None);

        response.Outcome.Should().Be(AiCallOutcome.Succeeded);
        fixture.Model.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_slow_model_times_out_into_the_fallback()
    {
        var fixture = new Fixture(o => o.TimeoutSeconds = 1);
        fixture.Model.Hang();

        var response = await fixture.Gateway.AskAsync(Ask("ar", "مرحبا"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { Language = "ar", IsFallback = true, Outcome = AiCallOutcome.TimedOut });
    }

    [Fact]
    public async Task The_whole_call_has_one_deadline()
    {
        var fixture = new Fixture(o =>
        {
            o.TimeoutSeconds = 30;
            o.TotalBudgetSeconds = 1;
        });
        fixture.Model.Hang();

        var response = await fixture.Gateway.AskAsync(Ask("en", "hello"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { IsFallback = true, Outcome = AiCallOutcome.TimedOut });
        fixture.Log.Records.Single().Outcome.Should().Be(AiCallOutcome.TimedOut);
    }

    [Fact]
    public async Task A_caller_cancellation_is_not_swallowed()
    {
        var fixture = new Fixture();
        fixture.Model.Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = () => fixture.Gateway.AskAsync(Ask("en", "hello"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Long_history_turns_are_capped()
    {
        var fixture = new Fixture(o => o.MaxMessageChars = 100);
        fixture.Model.Reply("Thank you for sharing all of that. What stands out most to you?");

        await fixture.Gateway.AskAsync(Ask("en", "ok", [new(ChatRole.User, new string('x', 500) + " end")]), CancellationToken.None);

        fixture.Model.Requests.Single().Messages[0].Text.Should().HaveLength(100 + "\n\nok".Length).And.Contain("end");
    }

    [Fact]
    public async Task Repeated_failures_open_the_circuit_and_stop_calling_the_model()
    {
        var fixture = new Fixture(o =>
        {
            o.MaxRetries = 0;
            o.BreakerMinimumThroughput = 2;
        });
        for (var i = 0; i < 2; i++)
        {
            fixture.Model.Fail(new TransientModelException("down"));
            (await fixture.Gateway.AskAsync(Ask("en", "hello"), CancellationToken.None)).Outcome.Should().Be(AiCallOutcome.Failed);
        }

        var response = await fixture.Gateway.AskAsync(Ask("en", "hello"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { IsFallback = true, Outcome = AiCallOutcome.CircuitOpen });
        fixture.Model.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task History_is_trimmed_starts_with_the_user_and_is_deidentified()
    {
        var fixture = new Fixture();
        fixture.Model.Reply("It sounds like Michael's words stayed with you. What would you like him to understand?");
        var history = new List<ChatTurn>
        {
            new(ChatRole.Assistant, "Hi, how are you today?"),
            new(ChatRole.User, "I argued with Michael"),
            new(ChatRole.Assistant, "That sounds painful."),
            new(ChatRole.User, "He called me lazy."),
        };

        await fixture.Gateway.AskAsync(Ask("en", "and then he left", history), CancellationToken.None);

        var messages = fixture.Model.Requests.Single().Messages;
        messages.Select(m => m.Role).Should().Equal(ChatRole.User, ChatRole.Assistant, ChatRole.User);
        messages[0].Text.Should().Be("I argued with <PERSON_1>");
        messages[^1].Text.Should().Be("He called me lazy.\n\nand then he left");
    }

    [Fact]
    public async Task A_failing_call_log_does_not_cost_the_patient_their_reply()
    {
        var fixture = new Fixture();
        fixture.Log.Throw = true;
        fixture.Model.Reply("I'm here with you. What would help most right now?");

        var response = await fixture.Gateway.AskAsync(Ask("en", "hi"), CancellationToken.None);

        response.IsFallback.Should().BeFalse();
    }

    [Fact]
    public async Task Empty_or_oversized_messages_are_rejected_with_a_localizable_code()
    {
        var fixture = new Fixture();

        var empty = () => fixture.Gateway.AskAsync(Ask("ar", " "), CancellationToken.None);
        var huge = () => fixture.Gateway.AskAsync(Ask("en", new string('a', 5000)), CancellationToken.None);

        (await empty.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.AiMessageInvalid);
        (await huge.Should().ThrowAsync<DomainException>()).Which.Code.Should().Be(ErrorCodes.AiMessageInvalid);
        fixture.Model.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_an_api_key_the_gateway_serves_the_fallback()
    {
        var fixture = new Fixture(model: new NotConfiguredLanguageModel("claude-opus-5-5"));

        var response = await fixture.Gateway.AskAsync(Ask("ar", "مرحبا"), CancellationToken.None);

        response.Should().BeEquivalentTo(new { IsFallback = true, Outcome = AiCallOutcome.Failed });
    }

    [Theory]
    [InlineData("companion", "ar")]
    [InlineData("companion", "en")]
    [InlineData("intake", "ar")]
    [InlineData("intake", "en")]
    [InlineData("intake-summary", "ar")]
    [InlineData("intake-summary", "en")]
    [InlineData("intake-hypotheses", "ar")]
    [InlineData("intake-hypotheses", "en")]
    [InlineData("journal-reflection", "ar")]
    [InlineData("journal-reflection", "en")]
    public async Task The_demo_model_passes_validation_in_the_patients_language(string purpose, string language)
    {
        var fixture = new Fixture(model: new DemoLanguageModel());
        var message = language == "ar" ? "أشعر بضغط كبير في العمل ولا أنام جيدًا" : "I feel a lot of pressure at work and I don't sleep well";

        var response = await fixture.Gateway.AskAsync(new AiRequest(Pseudonym, purpose, language, message, [], KnownIdentifiers.None), CancellationToken.None);

        response.Should().BeEquivalentTo(new { Language = language, IsFallback = false, Outcome = AiCallOutcome.Succeeded });
        if (purpose == "intake-summary")
        {
            response.Text.Split("\n---\n").Should().HaveCount(4);
        }
    }

    private static AiRequest Ask(string language, string message, IReadOnlyList<ChatTurn>? history = null) =>
        new(Pseudonym, "companion", language, message, history ?? [], KnownIdentifiers.None);

    private sealed class Fixture
    {
        public Fixture(Action<AiGatewayOptions>? configure = null, ILanguageModel? model = null)
        {
            var options = new AiGatewayOptions { RetryDelayMs = 0 };
            configure?.Invoke(options);
            var wrapped = Options.Create(options);
            Gateway = new AiGatewayService(
                Prompts,
                new Deidentifier(new PatternPiiDetector(), [], NullLogger<Deidentifier>.Instance),
                new OutputValidator(Options.Create(new OutputValidatorOptions())),
                model ?? Model,
                new AiResiliencePipeline(wrapped, TimeProvider.System),
                Log,
                new TestClock(DateTimeOffset.UnixEpoch),
                wrapped,
                NullLogger<AiGatewayService>.Instance);
        }

        public FakeModel Model { get; } = new();

        public FakeCallLog Log { get; } = new();

        public AiGatewayService Gateway { get; }
    }

    private sealed class FakeModel : ILanguageModel
    {
        private readonly Queue<Func<CancellationToken, Task<LanguageModelReply>>> steps = new();

        public List<LanguageModelRequest> Requests { get; } = [];

        public string ModelId => "fake-model";

        public void Reply(string text, LanguageModelStop stop = LanguageModelStop.Completed) =>
            steps.Enqueue(_ => Task.FromResult(new LanguageModelReply(text, stop, ModelId, 120, 40)));

        public void Fail(Exception ex) => steps.Enqueue(_ => Task.FromException<LanguageModelReply>(ex));

        public void Hang() => steps.Enqueue(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException("unreachable");
        });

        public Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return steps.Dequeue()(cancellationToken);
        }
    }

    private sealed class FakeCallLog : IAiCallLog
    {
        public List<AiCallRecord> Records { get; } = [];

        public bool Throw { get; set; }

        public Task RecordAsync(AiCallRecord record, CancellationToken cancellationToken)
        {
            if (Throw)
            {
                throw new InvalidOperationException("db down");
            }

            Records.Add(record);
            return Task.CompletedTask;
        }
    }
}
