using System.Text;
using System.Text.RegularExpressions;

namespace Tamaneen.AiGateway.Deidentification;

/// <summary>
/// Conversation-scoped mapping between personal values and the placeholders the model sees, e.g. "سارة" ↔
/// &lt;PERSON_1&gt;. The same value always gets the same placeholder, so the model can still follow who is who.
/// Lives only in memory for one gateway call; it is never logged or persisted.
/// </summary>
public sealed partial class PiiTokenMap
{
    private readonly Dictionary<string, string> tokenByValue = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (string Value, PiiType Type)> valueByToken = new(StringComparer.Ordinal);
    private readonly Dictionary<PiiType, int> counters = new();

    public int Count => valueByToken.Count;

    public string TokenFor(string value, PiiType type)
    {
        var key = $"{type}:{value.Trim()}";
        if (tokenByValue.TryGetValue(key, out var existing))
        {
            return existing;
        }

        counters[type] = counters.GetValueOrDefault(type) + 1;
        var token = $"<{Label(type)}_{counters[type]}>";
        tokenByValue[key] = token;
        valueByToken[token] = (value.Trim(), type);
        return token;
    }

    public bool IsKnownToken(string token) => valueByToken.ContainsKey(token);

    /// <summary>Placeholders of any shape (known or invented) in a text.</summary>
    public static IEnumerable<string> FindTokens(string text) => Token().Matches(text).Select(m => m.Value);

    /// <summary>
    /// Puts personal values back into a model reply for display to the patient who wrote them. Names, places and
    /// dates are restored so the reply reads naturally; numbers and addresses that could be misused (phone, card,
    /// IBAN, ID, e-mail, link, IP) are never echoed back and become a neutral phrase instead.
    /// </summary>
    public string Rehydrate(string text, string language)
    {
        var arabic = language == "ar";
        return Token().Replace(text, m =>
        {
            if (!valueByToken.TryGetValue(m.Value, out var entry))
            {
                return m.Value;
            }

            return entry.Type switch
            {
                PiiType.Person or PiiType.Address or PiiType.Date => entry.Value,
                PiiType.Phone => arabic ? "رقم الهاتف" : "the phone number",
                PiiType.Email => arabic ? "البريد الإلكتروني" : "the email address",
                PiiType.Url => arabic ? "الرابط" : "the link",
                PiiType.CreditCard or PiiType.Iban => arabic ? "بيانات الحساب" : "the account details",
                PiiType.NationalId => arabic ? "رقم الهوية" : "the ID number",
                _ => arabic ? "المعلومة الخاصة" : "the private detail",
            };
        });
    }

    /// <summary>
    /// Defuses placeholder-shaped text typed by the user ("&lt;PERSON_1&gt;") so it can never be mistaken for a
    /// real placeholder and rehydrated into someone else's data.
    /// </summary>
    public static string Escape(string text) => Token().Replace(text, m => $"‹{m.Value[1..^1]}›");

    /// <summary>Original values that must never appear verbatim in model output.</summary>
    internal IEnumerable<(string Value, PiiType Type)> Values => valueByToken.Values;

    private static string Label(PiiType type) => type switch
    {
        PiiType.Person => "PERSON",
        PiiType.Phone => "PHONE",
        PiiType.Email => "EMAIL",
        PiiType.NationalId => "ID",
        PiiType.Iban => "IBAN",
        PiiType.CreditCard => "CARD",
        PiiType.Url => "URL",
        PiiType.IpAddress => "IP",
        PiiType.Address => "PLACE",
        PiiType.Date => "DATE",
        _ => "PII",
    };

    [GeneratedRegex(@"<(?:PERSON|PHONE|EMAIL|ID|IBAN|CARD|URL|IP|PLACE|DATE|PII)_\d{1,4}>", RegexOptions.CultureInvariant)]
    private static partial Regex Token();
}
