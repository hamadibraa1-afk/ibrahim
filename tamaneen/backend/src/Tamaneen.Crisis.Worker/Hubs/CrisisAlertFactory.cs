using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Tamaneen.Crisis.Gate;
using Tamaneen.Hosting;
using Tamaneen.SharedKernel;

namespace Tamaneen.Crisis.Worker.Hubs;

/// <summary>Writes console messages in a given language. Used for live pushes and for the open-events list.</summary>
public sealed class CrisisAlertFactory
{
    private readonly IStringLocalizer<CrisisResource> _text;
    private readonly RuleCategoryIndex _categories;
    private readonly CrisisSlaOptions _sla;

    public CrisisAlertFactory(IStringLocalizer<CrisisResource> text, RuleCategoryIndex categories, IOptions<CrisisSlaOptions> sla)
    {
        _text = text;
        _categories = categories;
        _sla = sla.Value;
    }

    public static string Number(CrisisEvent e) => $"CE-{e.Number}";

    /// <summary>Localized name of where the words came from ("intake chat", "homework note"…), in the current culture.</summary>
    public string SourceLabel(string source)
    {
        var label = _text[$"source.{source}"];
        return label.ResourceNotFound ? _text["source.system"].Value : label.Value;
    }

    public CrisisAlertMessage Create(CrisisEvent e, CrisisUpdateKind? update, string language, string? acknowledgedByName = null) =>
        LocalizationSetup.WithLanguage(language, () =>
        {
            var level = _text[$"level.{(int)e.Level}"].Value;
            var patientLanguage = _text[$"language.{e.Language}"].Value;
            var tier = _text[$"tier.{Math.Min((int)e.EscalationTier, 2)}"].Value;
            var minutes = (int)Math.Ceiling(_sla.AcknowledgeWithin(e.Level).TotalMinutes);
            var (kind, title) = update switch
            {
                null => ("opened", _text["alert.opened.title", level, e.PseudonymId].Value),
                CrisisUpdateKind.Raised => ("raised", _text["alert.raised.title", level, e.PseudonymId].Value),
                CrisisUpdateKind.Escalated => ("escalated", _text["alert.escalated.title", tier, e.PseudonymId].Value),
                CrisisUpdateKind.Acknowledged => ("acknowledged", _text["alert.acknowledged.title", e.PseudonymId].Value),
                CrisisUpdateKind.Closed => ("closed", _text["alert.closed.title", e.PseudonymId].Value),
                CrisisUpdateKind.HandedOver => ("updated", _text["alert.handed_over.title", e.PseudonymId].Value),
                _ => throw new ArgumentOutOfRangeException(nameof(update)),
            };
            var categories = _categories.CategoriesOf(e.TriggerRuleIds).Select(c => _text[$"category.{c}"].Value).ToList();
            return new CrisisAlertMessage(
                e.Id,
                kind,
                e.PseudonymId,
                (int)e.Level,
                level,
                title,
                _text["alert.opened.body", minutes, patientLanguage].Value,
                e.Language,
                patientLanguage,
                e.EscalationTier,
                tier,
                e.Status.ToString(),
                e.OpenedAtUtc,
                e.AckDeadlineUtc,
                categories,
                SupportedLanguages.Direction(language),
                Number(e),
                e.Source,
                SourceLabel(e.Source),
                acknowledgedByName);
        });
}
