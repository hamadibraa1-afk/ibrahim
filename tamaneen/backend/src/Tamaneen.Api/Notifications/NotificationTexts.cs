using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using Tamaneen.Hosting;

namespace Tamaneen.Api.Notifications;

/// <summary>
/// Writes notification text from the catalog in the reader's language. In-app text may name the therapist and the
/// time; SMS and e-mail text never carries clinical words or a name (guide §10.4, S49): only "you have an
/// appointment" and the time, or a neutral "open the app" line. A template whose value is missing falls back to
/// its .plain variant, then to the generic line, so a kind from another module never shows raw braces.
/// </summary>
public sealed partial class NotificationTexts(IStringLocalizer<SharedResource> text)
{
    public const string Generic = "notify.generic";
    public const string ExternalDefault = "notify.sms.default";
    public const string ExternalNeutral = "notify.sms.neutral";

    public string InApp(string kind, IReadOnlyDictionary<string, string> data, string language, string timeZone) =>
        LocalizationSetup.WithLanguage(language, () =>
        {
            var values = Values(data, language, timeZone);
            return Fill(Lookup("notify." + kind), values) ?? Fill(Lookup("notify." + kind + ".plain"), values) ?? text[Generic].Value;
        });

    public string External(string kind, IReadOnlyDictionary<string, string> data, string language, string timeZone, bool neutral) =>
        LocalizationSetup.WithLanguage(language, () =>
        {
            if (neutral)
            {
                return text[ExternalNeutral].Value;
            }

            var values = Values(data, language, timeZone);
            return Fill(Lookup("notify.sms." + kind), values) ?? text[ExternalDefault].Value;
        });

    /// <summary>"الأحد 26 أكتوبر 4:00 م" / "Sunday 26 October 4:00 PM" in the reader's zone, Latin digits.</summary>
    public static string FormatTime(DateTimeOffset utc, string language, string timeZone)
    {
        var zone = FindZone(timeZone);
        var local = TimeZoneInfo.ConvertTime(utc, zone);
        var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "ar-AE");
        return local.ToString("dddd d MMMM h:mm tt", culture);
    }

    public static TimeZoneInfo FindZone(string? timeZone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(timeZone) ? "Asia/Riyadh" : timeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Riyadh");
        }
    }

    private string? Lookup(string key)
    {
        var value = text[key];
        return value.ResourceNotFound ? null : value.Value;
    }

    private static Dictionary<string, string> Values(IReadOnlyDictionary<string, string> data, string language, string timeZone)
    {
        var values = new Dictionary<string, string>(data, StringComparer.Ordinal);
        if (data.TryGetValue("startsAt", out var starts) && DateTimeOffset.TryParse(starts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
        {
            values["time"] = FormatTime(at, language, timeZone);
        }

        if (!values.ContainsKey("therapist") && data.TryGetValue("therapistName", out var name))
        {
            values["therapist"] = name;
        }

        if (!values.ContainsKey("patient") && data.TryGetValue("pseudonymId", out var pseudonym))
        {
            values["patient"] = pseudonym;
        }

        return values;
    }

    /// <summary>Null when the template is missing or names a value the data does not have.</summary>
    internal static string? Fill(string? template, IReadOnlyDictionary<string, string> values)
    {
        if (template is null)
        {
            return null;
        }

        var missing = false;
        var result = Placeholder().Replace(template, m =>
        {
            if (values.TryGetValue(m.Groups[1].Value, out var v) && !string.IsNullOrWhiteSpace(v))
            {
                return v;
            }

            missing = true;
            return string.Empty;
        });
        return missing ? null : result;
    }

    [GeneratedRegex(@"\{([a-zA-Z]+)\}")]
    private static partial Regex Placeholder();
}
