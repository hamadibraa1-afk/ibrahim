using System.Globalization;
using System.Net;
using System.Text;
using ProposalSystem.Api.Domain;

namespace ProposalSystem.Api.Documents;

/// <summary>
/// Printable HTML documents (the browser's "Save as PDF" produces the PDF). Every value is
/// HTML-encoded; the documents contain no script.
/// </summary>
public static class DocumentRenderer
{
    private const string Styles = """
        <style>
          @page { size: A4; margin: 16mm; }
          body { font-family: 'Tajawal','Segoe UI',Tahoma,sans-serif; color:#1f2a24; direction:rtl; margin:0; padding:24px; }
          h1 { font-size:18px; margin:0 0 4px; color:#0f5132; }
          .meta { color:#6b7280; font-size:12px; margin-bottom:16px; }
          table { width:100%; border-collapse:collapse; margin-bottom:14px; font-size:13px; }
          th, td { border:1px solid #cfd8d3; padding:8px 10px; text-align:right; vertical-align:top; }
          th { background:#eef4f0; width:28%; }
          .section { background:#0f5132; color:#fff; padding:6px 10px; font-weight:bold; font-size:13px; margin-top:16px; }
          .box { border:1px solid #cfd8d3; padding:10px; white-space:pre-line; font-size:13px; line-height:1.9; }
          .muted { color:#6b7280; }
          .cert { text-align:center; border:6px double #0f5132; padding:48px 32px; margin-top:24px; }
          .cert h2 { font-size:28px; color:#0f5132; margin:0 0 18px; }
          .cert .name { font-size:24px; font-weight:bold; margin:18px 0; color:#b36b2c; }
        </style>
        """;

    private static readonly Dictionary<ProposalClassification, string> Classifications = new()
    {
        [ProposalClassification.Excellent] = "متميز",
        [ProposalClassification.VeryGood] = "جيد جداً",
        [ProposalClassification.Good] = "جيد",
        [ProposalClassification.Acceptable] = "مقبول",
        [ProposalClassification.NotApplicable] = "غير قابل للتطبيق",
    };

    /// <summary>
    /// SCI-M-02-F-01. The employee's name is always withheld on the exported form. The contact
    /// details (employee number, phone, email) identify the person just as well, so a blind
    /// reviewer gets those masked too until the reveal.
    /// </summary>
    public static string OfficialForm(Proposal p, IReadOnlyList<FormField> impactFields, bool seesIdentity, bool seesDepartment)
    {
        const string masked = "— محجوب (مراجعة معمّاة) —";
        var sb = Begin($"نموذج مقترح — {p.ProposalCode}");
        sb.Append("<h1>نموذج تقديم مقترح</h1><div class=\"meta\">SCI-M-02-F-01 · الإصدار 02 · جمعية الشارقة الخيرية</div>");
        sb.Append("<div class=\"section\">بيانات مقدّم المقترح</div><table>");
        Row(sb, "اسم الموظف", "— محجوب حفاظاً على الخصوصية —");
        Row(sb, "الرقم الوظيفي", seesIdentity ? p.Submitter.UserCode : masked);
        Row(sb, "الإدارة / القسم", seesDepartment ? p.Department : masked);
        Row(sb, "الهاتف", seesIdentity ? p.Submitter.PhoneNumber : masked);
        Row(sb, "البريد الإلكتروني", seesIdentity ? p.Submitter.Email : masked);
        Row(sb, "تاريخ التقديم", p.SubmittedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        sb.Append("</table>");

        sb.Append("<div class=\"section\">بيانات المقترح</div><table>");
        Row(sb, "رقم المقترح", p.ProposalCode);
        Row(sb, "عنوان المقترح", p.Title);
        sb.Append("</table>");
        Box(sb, "آلية تطبيق المقترح", p.ImplementationMechanism);
        Box(sb, "أسباب تقديم المقترح", p.SubmissionReasons);

        var selected = impactFields
            .Where(f => p.FieldValues.Any(v => v.FieldId == f.Id && v.Value == "true"))
            .Select(f => "☑ " + f.LabelAr);
        Box(sb, "أثر تطبيق المقترح", string.Join("\n", selected.DefaultIfEmpty("—")));

        foreach (var v in p.FieldValues.Where(v => v.Field.Section is FormSections.SuggestionData or FormSections.SuggestionDetails).OrderBy(v => v.Field.SortOrder))
            Box(sb, v.Field.LabelAr, v.Value ?? "—");

        if (p.Classification is { } cls)
        {
            sb.Append("<div class=\"section\">نتيجة لجنة دراسة المقترحات</div><table>");
            Row(sb, "التصنيف", Classifications[cls]);
            Row(sb, "الدراسة", p.CommitteeStudy);
            Row(sb, "التوصية", p.CommitteeRecommendation);
            sb.Append("</table>");
        }
        if (p.ExecutiveDecision is { } decision)
        {
            sb.Append("<div class=\"section\">قرار الإدارة التنفيذية</div><table>");
            Row(sb, "القرار", decision == ExecutiveDecisionValue.Accepted ? "الموافقة" : "عدم الموافقة");
            Row(sb, "الملاحظات", p.ExecutiveDecisionNotes);
            Row(sb, "التاريخ", p.ExecutiveDecisionAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            sb.Append("</table>");
        }
        return End(sb);
    }

    public static string Minutes(Proposal p)
    {
        var sb = Begin($"محضر اللجنة — {p.ProposalCode}");
        sb.Append("<h1>محضر لجنة دراسة المقترحات</h1>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"meta\">{Enc(p.ProposalCode)} · {Enc(p.Title)}</div><table>");
        Row(sb, "التصنيف", p.Classification is { } c ? Classifications[c] : "—");
        Row(sb, "الدراسة", p.CommitteeStudy);
        Row(sb, "التوصية", p.CommitteeRecommendation);
        sb.Append("</table><div class=\"section\">توقيعات الأعضاء</div><table><tr><th>العضو</th><th>التوقيع</th><th>التاريخ</th></tr>");
        foreach (var v in p.Votes.OrderBy(v => v.Member.ArabicName))
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"<tr><td>{Enc(v.Member.ArabicName)}</td><td>{(v.Signed ? "✓ وقّع" : "<span class=\"muted\">لم يوقّع</span>")}</td><td>{v.SignedAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "—"}</td></tr>");
        }
        sb.Append("</table>");
        return End(sb);
    }

    public static string Certificate(Proposal p, string recipientName)
    {
        var sb = Begin("شهادة شكر وتقدير");
        sb.Append("<div class=\"cert\"><h2>شهادة شكر وتقدير</h2>");
        sb.Append("<div>تتقدّم جمعية الشارقة الخيرية بخالص الشكر والتقدير إلى</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"name\">{Enc(recipientName)}</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<div>تقديراً لمقترحه المتميّز «{Enc(p.Title)}» ({Enc(p.ProposalCode)}) الذي اعتمدته الإدارة التنفيذية</div>");
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"meta\" style=\"margin-top:24px\">{p.ExecutiveDecisionAt?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</div></div>");
        return End(sb);
    }

    private static StringBuilder Begin(string title) =>
        new StringBuilder()
            .Append("<!doctype html><html lang=\"ar\" dir=\"rtl\"><head><meta charset=\"utf-8\"><title>")
            .Append(Enc(title))
            .Append("</title>")
            .Append(Styles)
            .Append("</head><body>");

    private static string End(StringBuilder sb) => sb.Append("</body></html>").ToString();

    private static void Row(StringBuilder sb, string label, string? value) =>
        sb.Append(CultureInfo.InvariantCulture, $"<tr><th>{Enc(label)}</th><td>{Enc(string.IsNullOrWhiteSpace(value) ? "—" : value)}</td></tr>");

    private static void Box(StringBuilder sb, string label, string? value) =>
        sb.Append(CultureInfo.InvariantCulture, $"<div class=\"section\">{Enc(label)}</div><div class=\"box\">{Enc(string.IsNullOrWhiteSpace(value) ? "—" : value)}</div>");

    private static string Enc(string? s) => WebUtility.HtmlEncode(s ?? "");
}
