using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tamaneen.AiGateway.Deidentification;
using Tamaneen.SharedKernel;

namespace Tamaneen.AiGateway.Validation;

public enum OutputViolation
{
    Empty = 0,
    TooLong = 1,
    WrongLanguage = 2,
    UnknownPlaceholder = 3,
    PersonalDataEcho = 4,
    ContactDetails = 5,
    Diagnosis = 6,
    Medication = 7,
    HarmfulMethod = 8,
    Truncated = 9,
}

public sealed record OutputValidation(IReadOnlyList<OutputViolation> Violations)
{
    public bool Passed => Violations.Count == 0;

    /// <summary>True when the only problem is the language, which one rewrite request can fix.</summary>
    public bool OnlyLanguage => Violations.Count == 1 && Violations[0] == OutputViolation.WrongLanguage;
}

public sealed class OutputValidatorOptions
{
    /// <summary>Share of letters that must be in the requested script.</summary>
    public double MinScriptShare { get; set; } = 0.7;
}

/// <summary>
/// Last line of defence between the model and a patient. Runs on the de-identified reply (before placeholders
/// are filled back in) and rejects anything outside the companion's remit; a rejected reply is never shown,
/// the localized fallback is. Rules are deliberately blunt: a false rejection costs one generic message.
/// </summary>
public sealed partial class OutputValidator(IOptions<OutputValidatorOptions> options)
{
    private static readonly PatternPiiDetector Contacts = new();

    public async Task<OutputValidation> ValidateAsync(string reply, string language, int maxChars, bool truncated, PiiTokenMap map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(map);
        var violations = new List<OutputViolation>();
        if (string.IsNullOrWhiteSpace(reply))
        {
            return new OutputValidation([OutputViolation.Empty]);
        }

        if (truncated)
        {
            violations.Add(OutputViolation.Truncated);
        }

        if (reply.Length > maxChars)
        {
            violations.Add(OutputViolation.TooLong);
        }

        if (!InLanguage(reply, language, options.Value.MinScriptShare))
        {
            violations.Add(OutputViolation.WrongLanguage);
        }

        if (PiiTokenMap.FindTokens(reply).Any(t => !map.IsKnownToken(t)))
        {
            violations.Add(OutputViolation.UnknownPlaceholder);
        }

        if (map.Values.Any(v => v.Value.Length >= 3 && reply.Contains(v.Value, StringComparison.OrdinalIgnoreCase)))
        {
            violations.Add(OutputViolation.PersonalDataEcho);
        }

        var found = await Contacts.DetectAsync(PiiTokenMap.FindTokens(reply).Aggregate(reply, (s, t) => s.Replace(t, " ", StringComparison.Ordinal)), language, KnownIdentifiers.None, cancellationToken).ConfigureAwait(false);
        if (found.Any(s => s.Type is PiiType.Phone or PiiType.Email or PiiType.Url or PiiType.IpAddress or PiiType.Iban or PiiType.CreditCard or PiiType.NationalId))
        {
            violations.Add(OutputViolation.ContactDetails);
        }

        var folded = PatternPiiDetector.ArabicKey(reply);
        if (Diagnosis().IsMatch(reply) || DiagnosisAr().IsMatch(folded))
        {
            violations.Add(OutputViolation.Diagnosis);
        }

        if (Medication().IsMatch(reply) || MedicationAr().IsMatch(folded) || Dose().IsMatch(reply))
        {
            violations.Add(OutputViolation.Medication);
        }

        if (Harm().IsMatch(reply) || HarmAr().IsMatch(folded))
        {
            violations.Add(OutputViolation.HarmfulMethod);
        }

        return new OutputValidation(violations);
    }

    /// <summary>Counts Arabic-script letters against Latin letters, ignoring placeholders, digits and punctuation.</summary>
    internal static bool InLanguage(string text, string language, double minShare)
    {
        var stripped = PiiTokenMap.FindTokens(text).Aggregate(text, (s, t) => s.Replace(t, " ", StringComparison.Ordinal));
        int arabic = 0, latin = 0;
        foreach (var ch in stripped)
        {
            if (ch is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ' or >= 'ﭐ' and <= '﷿' or >= 'ﹰ' and <= '﻿')
            {
                if (char.IsLetter(ch))
                {
                    arabic++;
                }
            }
            else if (ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= 'À' and <= 'ɏ')
            {
                latin++;
            }
        }

        var total = arabic + latin;
        if (total == 0)
        {
            return false;
        }

        var share = SupportedLanguages.Normalize(language) == SupportedLanguages.Arabic ? (double)arabic / total : (double)latin / total;
        return share >= minShare;
    }

    // "you have depression", "you might have bipolar", "my diagnosis is".
    [GeneratedRegex(@"\b(?:you(?:'re|\s+are|\s+have|\s+might\s+have|\s+may\s+have|\s+probably\s+have|\s+suffer\s+from|\s+are\s+suffering\s+from|\s+seem\s+to\s+have|\s+likely\s+have|\s+clearly\s+have)\s+(?:an?\s+|clinical\s+|severe\s+|mild\s+|major\s+)*(?:depression|depressive\s+disorder|bipolar|ptsd|ocd|adhd|schizophreni\w*|psychosis|psychotic|borderline|personality\s+disorder|anxiety\s+disorder|panic\s+disorder|eating\s+disorder|anorexia|bulimia|autism|autistic)|(?:my|this|a)\s+diagnosis\s+(?:is|of|would)|i\s+(?:would\s+)?diagnose)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Diagnosis();

    // Folded Arabic (أ→ا, ة→ه, ى→ي; ئ is kept, hence [ئي]): "انت مصاب بالاكتئاب", "لديك اضطراب", "تشخيصك", "تعاني من ثنائي القطب".
    [GeneratedRegex(@"(?:انت|انتي|إنت)?\s*(?:مصاب|مصابه)\s+ب|(?:لديك|عندك|تعاني\s+من|تعانين\s+من)\s+(?:اضطراب|اكت[ئي]اب\s+سريري|الاكت[ئي]اب\s+السريري|ثنا[ئي]ي\s+القطب|الفصام|فصام|ذهان|الذهان|وسواس\s+قهري|الوسواس\s+القهري|كرب\s+ما\s+بعد\s+الصدمه|اضطراب\s+الشخصيه|توحد)|تشخيصك|التشخيص\s+هو|اشخصك|يبدو\s+انك\s+مصاب", RegexOptions.CultureInvariant)]
    private static partial Regex DiagnosisAr();

    [GeneratedRegex(@"\b(?:sertraline|zoloft|fluoxetine|prozac|escitalopram|lexapro|cipralex|citalopram|paroxetine|paxil|seroxat|venlafaxine|effexor|duloxetine|cymbalta|bupropion|wellbutrin|mirtazapine|remeron|trazodone|amitriptyline|alprazolam|xanax|diazepam|valium|lorazepam|ativan|clonazepam|rivotril|klonopin|quetiapine|seroquel|olanzapine|zyprexa|risperidone|risperdal|aripiprazole|abilify|lithium|lamotrigine|lamictal|valproate|depakote|methylphenidate|ritalin|concerta|adderall|zolpidem|ambien|melatonin|propranolol|pregabalin|lyrica|gabapentin|tramadol|paracetamol|acetaminophen|ibuprofen|antidepressants?|ssris?|snris?|benzodiazepines?|benzos|antipsychotics?|sleeping\s+pills?|mood\s+stabili[sz]ers?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Medication();

    [GeneratedRegex(@"سيرترالين|زولفت|فلوكستين|فلوكسيتين|بروزاك|اسيتالوبرام|سيبرالكس|سيتالوبرام|باروكستين|زيروكسات|فينلافاكسين|افكسور|دولوكستين|سيمبالتا|بوبروبيون|ميرتازابين|زاناكس|البرازولام|ديازيبام|فاليوم|لورازيبام|كلونازيبام|ريفوتريل|كويتيابين|سيروكويل|اولانزابين|ريسبيريدون|ابيليفاي|ليثيوم|لاموتريجين|ريتالين|كونسيرتا|ميلاتونين|بروبرانولول|بريجابالين|ليريكا|ترامادول|باراسيتامول|بنادول|مضادات\s+الاكت[ئي]اب|مضاد\s+اكت[ئي]اب|مهد[ئي]|مهد[ئي]ات|حبوب\s+منومه|حبوب\s+النوم|منوم", RegexOptions.CultureInvariant)]
    private static partial Regex MedicationAr();

    [GeneratedRegex(@"\b\d+(?:[.,]\d+)?\s?(?:mg|mcg|µg|ml|milligrams?|ملغ|ملغم|مغ|مجم|ملجم|ملل)\b|\d+\s?(?:mg|ملغ|مجم)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Dose();

    [GeneratedRegex(@"\b(?:lethal\s+dose|overdos\w*|how\s+(?:to|many)\s+(?:pills|tablets)|hang(?:ing)?\s+(?:yourself|myself|oneself)|noose|slit\w*\s+(?:your|my)?\s*wrists?|cut(?:ting)?\s+(?:deeper|your\s+wrists?|vertically)|jump(?:ing)?\s+(?:off|from)\s+(?:a\s+)?(?:bridge|building|roof)|carbon\s+monoxide|rat\s+poison|bleach|painless\s+(?:way|method)|ways?\s+to\s+(?:die|kill\s+yourself|end\s+(?:it|your\s+life)))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Harm();

    [GeneratedRegex(@"جرعه\s+(?:ال)?(?:زا[ئي]ده|قاتله|مميته)|كم\s+حبه|عدد\s+الحبوب|الشنق|تشنق|حبل\s+المشنقه|قطع\s+(?:الوريد|الشريان|المعصم)|اقطع\s+(?:الوريد|اعمق)|القفز\s+من|اقفز\s+من|سم\s+الف[ئي]ران|الكلور|طريقه\s+(?:للموت|للانتحار|غير\s+مؤلمه)|طرق\s+(?:الانتحار|للانتحار|الموت)|كيف\s+(?:انتحر|تنتحر)", RegexOptions.CultureInvariant)]
    private static partial Regex HarmAr();
}
