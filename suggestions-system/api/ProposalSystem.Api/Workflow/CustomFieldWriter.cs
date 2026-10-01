using System.Globalization;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Workflow;

/// <summary>
/// Validates and stores values for administrator-defined form fields. Only active, non-system
/// fields of the given sections are accepted; unknown keys are ignored rather than stored.
/// </summary>
public sealed class CustomFieldWriter(AppDbContext db)
{
    private const int MaxValueLength = 4000;

    public async Task ApplyAsync(Proposal p, IReadOnlyDictionary<string, string?>? values, string[] sections, bool enforceRequired, CancellationToken ct)
    {
        values ??= new Dictionary<string, string?>();
        var fields = await db.FormFields
            .Where(f => f.IsActive && !f.IsSystem && sections.Contains(f.Section))
            .ToListAsync(ct);

        foreach (var field in fields)
        {
            values.TryGetValue(field.FieldKey, out var raw);
            var value = Normalize(field, raw?.Trim());

            if (enforceRequired && field.IsRequired && field.FieldType != FormFieldType.Checkbox && string.IsNullOrEmpty(value))
                throw ApiException.BadRequest($"الحقل \"{field.LabelAr}\" إلزامي.");

            var existing = p.FieldValues.FirstOrDefault(v => v.FieldId == field.Id);
            if (existing is null)
                p.FieldValues.Add(new ProposalFieldValue { FieldId = field.Id, Field = field, Value = value });
            else
                existing.Value = value;
        }

        if (enforceRequired && sections.Contains(FormSections.Impact))
        {
            var impactFields = fields.Where(f => f.Section == FormSections.Impact).ToList();
            if (impactFields.Count > 0 && !impactFields.Any(f => p.FieldValues.Any(v => v.FieldId == f.Id && v.Value == "true")))
                throw ApiException.BadRequest("الرجاء تحديد أثر واحد على الأقل لتطبيق المقترح.");
        }
    }

    private static string? Normalize(FormField field, string? value)
    {
        if (field.FieldType == FormFieldType.Checkbox)
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.Length > MaxValueLength)
            throw ApiException.BadRequest($"قيمة الحقل \"{field.LabelAr}\" طويلة جداً.");

        switch (field.FieldType)
        {
            case FormFieldType.Number when !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _):
                throw ApiException.BadRequest($"الحقل \"{field.LabelAr}\" يجب أن يكون رقماً.");
            case FormFieldType.Date when !DateOnly.TryParse(value, CultureInfo.InvariantCulture, out _):
                throw ApiException.BadRequest($"الحقل \"{field.LabelAr}\" يجب أن يكون تاريخاً صالحاً.");
            case FormFieldType.Select when !ProposalMapper.ParseOptions(field.OptionsJson).Contains(value):
                throw ApiException.BadRequest($"القيمة المختارة في \"{field.LabelAr}\" غير موجودة في القائمة.");
        }
        return value;
    }
}
