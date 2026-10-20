namespace Tamaneen.AiGateway.Model;

/// <summary>
/// A canned model for trying the product without an API key (<c>Anthropic:Demo=true</c>, never in Production).
/// It answers in the language the prompt demands, walks the intake goals one question at a time and writes the
/// four-paragraph summary from the patient's own (de-identified) words. Its replies still pass through the
/// gateway's validation like a real model's.
/// </summary>
public sealed class DemoLanguageModel : ILanguageModel
{
    private static readonly string[] IntakeAr =
    [
        "شكرًا لأنك شاركتني هذا. منذ متى تشعر بذلك؟",
        "أفهم. كيف يؤثر ذلك على نومك أو عملك أو علاقاتك هذه الأيام؟",
        "هذا متعب فعلًا. ما الذي تتمنى أن يتغير بعد الجلسات؟",
        "جميل أن يكون لديك هدف. ما الذي يساعدك قليلًا حين يشتد الأمر؟",
        "شكرًا لك. هل سبق أن تحدثت مع مختص نفسي من قبل؟",
        "شكرًا على صراحتك. لدينا الآن صورة جيدة، ويمكنك إنهاء المحادثة لترى الملخص وتصححه.",
    ];

    private static readonly string[] IntakeEn =
    [
        "Thank you for sharing that. How long have you been feeling this way?",
        "I see. How is it affecting your sleep, work or relationships these days?",
        "That sounds tiring. What would you like to be different after the sessions?",
        "That is a good thing to hope for. What helps you a little when it gets hard?",
        "Thank you. Have you talked with a mental-health professional before?",
        "Thank you for being so open. We have a good picture now; you can end the chat to see and correct the summary.",
    ];

    public string ModelId => "demo-model";

    public Task<LanguageModelReply> CompleteAsync(LanguageModelRequest request, CancellationToken cancellationToken)
    {
        var arabic = request.System.Contains("Arabic only", StringComparison.Ordinal);
        string text;
        if (request.System.Contains("exactly four short paragraphs", StringComparison.Ordinal))
        {
            text = Summary(request, arabic);
        }
        else if (request.System.Contains("intake assistant", StringComparison.Ordinal))
        {
            var asked = request.Messages.Count(m => m.Role == ChatRole.Assistant);
            var script = arabic ? IntakeAr : IntakeEn;
            text = script[Math.Min(asked, script.Length - 1)];
        }
        else if (request.System.Contains("journal entry", StringComparison.Ordinal))
        {
            text = arabic
                ? "يبدو أن هذا اليوم حمل لك مشاعر كثيرة، وكتابتك عنها خطوة لطيفة تجاه نفسك. ما الشيء الصغير الذي قد يريحك غدًا؟"
                : "It sounds like today brought a lot of feelings, and writing about them is a kind step for yourself. What small thing might help you tomorrow?";
        }
        else
        {
            text = arabic
                ? "أنا أسمعك. خذ نفسًا هادئًا معي، ثم أخبرني ما الذي يشغلك أكثر الآن."
                : "I hear you. Take a slow breath with me, then tell me what is on your mind most right now.";
        }

        return Task.FromResult(new LanguageModelReply(text, LanguageModelStop.Completed, ModelId, 0, 0));
    }

    private static string Summary(LanguageModelRequest request, bool arabic)
    {
        var words = request.Messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? string.Empty;
        var first = words.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        if (first.Length > 240)
        {
            first = first[..240];
        }

        return arabic
            ? $"{(first.Length > 0 ? $"شاركتنا أنك تمر بما يلي: {first}" : "شاركتنا ما يضايقك في الفترة الأخيرة.")}\n---\nأن تجد طرقًا تساعدك على الشعور بتحسن.\n---\nما ذكرته من أشخاص وأنشطة تساعدك.\n---\nسيراجع المختص تجربتك السابقة معك في الجلسة الأولى."
            : $"{(first.Length > 0 ? $"You told us you are going through this: {first}" : "You told us what has been troubling you lately.")}\n---\nTo find ways that help you feel better.\n---\nThe people and activities you mentioned that help you.\n---\nYour therapist will go over any past experience with you in the first session.";
    }
}
