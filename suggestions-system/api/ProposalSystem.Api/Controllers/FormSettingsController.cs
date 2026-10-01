using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Common;
using ProposalSystem.Api.Data;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Workflow;

namespace ProposalSystem.Api.Controllers;

public sealed record FormFieldDto(
    int Id, string FieldKey, string LabelAr, string? LabelEn, string? Placeholder, FormFieldType FieldType,
    string Section, bool IsRequired, bool IsActive, bool IsSystem, int SortOrder, IReadOnlyList<string> Options)
{
    public static FormFieldDto From(FormField f) => new(
        f.Id, f.FieldKey, f.LabelAr, f.LabelEn, f.Placeholder, f.FieldType, f.Section, f.IsRequired,
        f.IsActive, f.IsSystem, f.SortOrder, ProposalMapper.ParseOptions(f.OptionsJson));
}

public sealed record SaveFormFieldRequest(
    string FieldKey, string LabelAr, string? LabelEn, string? Placeholder, FormFieldType FieldType,
    string Section, bool IsRequired, bool IsActive, int SortOrder, List<string>? Options);

public sealed record ReorderRequest(List<int> OrderedIds);

/// <summary>
/// Dynamic form settings: the administrator shapes the submission form and the post-implementation
/// "actual impact" form (section ImpactMeasurement) without a code change.
/// </summary>
[ApiController]
[Route("api/form-settings")]
[Authorize]
public sealed partial class FormSettingsController(AppDbContext db) : ControllerBase
{
    /// <summary>The submission form cannot work without these, so they can't be switched off.</summary>
    private static readonly HashSet<string> CoreKeys = ["title", "implementationMechanism", "submissionReasons"];

    /// <summary>Every signed-in user reads the settings: they shape the forms they fill in.</summary>
    [HttpGet]
    public async Task<List<FormFieldDto>> GetAll([FromQuery] bool activeOnly, CancellationToken ct)
    {
        var q = db.FormFields.AsQueryable();
        if (activeOnly) q = q.Where(f => f.IsActive);
        return (await q.OrderBy(f => f.SortOrder).ThenBy(f => f.Id).ToListAsync(ct)).Select(FormFieldDto.From).ToList();
    }

    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<FormFieldDto> Create(SaveFormFieldRequest req, CancellationToken ct)
    {
        Validate(req);
        if (!KeyPattern().IsMatch(req.FieldKey ?? ""))
            throw ApiException.BadRequest("مفتاح الحقل يجب أن يبدأ بحرف إنجليزي ويحتوي حروفاً وأرقاماً فقط.");
        var key = req.FieldKey!;
        if (await db.FormFields.AnyAsync(f => f.FieldKey.ToLower() == key.ToLower(), ct))
            throw ApiException.Conflict("مفتاح الحقل مستخدم بالفعل.");

        var field = new FormField { FieldKey = key, IsSystem = false };
        Apply(field, req, allowStructural: true);
        db.FormFields.Add(field);
        await db.SaveChangesAsync(ct);
        return FormFieldDto.From(field);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<FormFieldDto> Update(int id, SaveFormFieldRequest req, CancellationToken ct)
    {
        Validate(req);
        var field = await db.FormFields.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw ApiException.NotFound("الحقل غير موجود.");
        if (field.IsSystem && CoreKeys.Contains(field.FieldKey) && !req.IsActive)
            throw ApiException.BadRequest("لا يمكن إخفاء حقول النظام الأساسية.");
        // Key, type and section of a system field are tied to a database column.
        Apply(field, req, allowStructural: !field.IsSystem);
        await db.SaveChangesAsync(ct);
        return FormFieldDto.From(field);
    }

    [HttpPost("reorder")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Reorder(ReorderRequest req, CancellationToken ct)
    {
        var fields = await db.FormFields.ToDictionaryAsync(f => f.Id, ct);
        for (var i = 0; i < req.OrderedIds.Count; i++)
        {
            if (fields.TryGetValue(req.OrderedIds[i], out var f))
                f.SortOrder = i;
        }
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Admin))]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var field = await db.FormFields.FirstOrDefaultAsync(f => f.Id == id, ct) ?? throw ApiException.NotFound("الحقل غير موجود.");
        if (field.IsSystem)
            throw ApiException.BadRequest("لا يمكن حذف حقول النظام.");
        if (await db.FieldValues.AnyAsync(v => v.FieldId == id && v.Value != null && v.Value != "" && v.Value != "false", ct))
            throw ApiException.Conflict("هذا الحقل يحمل بيانات في مقترحات سابقة. عطّله بدلاً من حذفه للحفاظ على السجلات.");
        db.FormFields.Remove(field);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static void Apply(FormField field, SaveFormFieldRequest req, bool allowStructural)
    {
        field.LabelAr = req.LabelAr.Trim();
        field.LabelEn = string.IsNullOrWhiteSpace(req.LabelEn) ? null : req.LabelEn.Trim();
        field.Placeholder = string.IsNullOrWhiteSpace(req.Placeholder) ? null : req.Placeholder.Trim();
        field.IsRequired = req.IsRequired;
        field.IsActive = req.IsActive;
        field.SortOrder = req.SortOrder;
        if (!allowStructural)
            return;

        field.Section = req.Section;
        // "Impact" is the grid of tick boxes on the submission form.
        field.FieldType = req.Section == FormSections.Impact ? FormFieldType.Checkbox : req.FieldType;
        var options = field.FieldType == FormFieldType.Select
            ? (req.Options ?? []).Select(o => o.Trim()).Where(o => o.Length > 0).Distinct().ToList()
            : [];
        if (field.FieldType == FormFieldType.Select && options.Count < 2)
            throw ApiException.BadRequest("القائمة المنسدلة تحتاج خيارين على الأقل.");
        field.OptionsJson = JsonSerializer.Serialize(options);
    }

    private static void Validate(SaveFormFieldRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LabelAr))
            throw ApiException.BadRequest("اسم الحقل بالعربية إلزامي.");
        if (!FormSections.All.Contains(req.Section))
            throw ApiException.BadRequest("القسم غير معروف.");
        if (!Enum.IsDefined(req.FieldType))
            throw ApiException.BadRequest("نوع الحقل غير معروف.");
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]{0,59}$")]
    private static partial Regex KeyPattern();
}
