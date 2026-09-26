using System.Globalization;
using System.Net;
using System.Text;
using FieldAttendance.Api.Common;
using FieldAttendance.Api.Data;
using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Features.Reports;

/// <summary>
/// HR reports as a formatted Arabic document.
///
/// Two outputs from one renderer: "print" returns a right-to-left page the browser prints
/// or saves as PDF, and "word" returns the same document as a .doc that Word opens directly.
/// This keeps Arabic shaping and direction correct — the usual failure of PDF libraries with
/// Arabic — without adding a rendering dependency.
/// </summary>
[ApiController]
[Route("api/hr/reports")]
[Authorize(Policy = HrPolicies.Read)]
public sealed class ReportsController(AppDbContext db, IClock clock) : ControllerBase
{
    [HttpGet("attendance")]
    public async Task<IActionResult> Attendance([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] Guid? departmentId, [FromQuery] string format = "print", CancellationToken ct = default)
    {
        if (to < from || to.DayNumber - from.DayNumber > 92)
            throw new DomainException("report.range", "Range must be 1–92 days.");

        var profiles = await db.EmployeeProfiles.AsNoTracking()
            .Where(p => departmentId == null || p.DepartmentId == departmentId).ToListAsync(ct);
        var ids = profiles.Select(p => p.UserId).ToList();

        var records = await db.AttendanceRecords.AsNoTracking()
            .Where(r => ids.Contains(r.EmployeeId) && r.ShiftDate >= from && r.ShiftDate <= to).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);

        var rows = profiles.Select(p =>
        {
            var days = records.Where(r => r.EmployeeId == p.UserId).ToList();
            var user = users.GetValueOrDefault(p.UserId);
            return new[]
            {
                user?.EmployeeNumber ?? "—", user?.FullName ?? "—", departments.GetValueOrDefault(p.DepartmentId, "—"),
                days.Count.ToString(CultureInfo.InvariantCulture),
                days.Count(d => d.CheckInAt != null).ToString(CultureInfo.InvariantCulture),
                days.Count(d => d.Status == AttendanceStatus.Absent).ToString(CultureInfo.InvariantCulture),
                days.Count(d => d.Status == AttendanceStatus.OnLeave).ToString(CultureInfo.InvariantCulture),
                Hours(days.Sum(d => d.NetWorkMinutes)), Hours(days.Sum(d => d.LateUnexcused)),
                Hours(days.Sum(d => d.EarlyUnexcused)), Hours(days.Sum(d => d.OvertimeMinutes)),
            };
        }).OrderBy(r => r[1], StringComparer.Ordinal).ToList();

        var headers = new[] { "الرقم الوظيفي", "الاسم", "الإدارة", "أيام الدوام", "أيام الحضور", "الغياب",
            "الإجازات", "صافي العمل", "التأخير", "الانصراف المبكر", "العمل الإضافي" };

        return Render("تقرير الحضور والانصراف", $"من {from} إلى {to}", headers, rows, format, $"attendance_{from}_{to}");
    }

    [HttpGet("payroll/{cycleId:guid}")]
    [Authorize(Policy = HrPolicies.Manage)]
    public async Task<IActionResult> Payroll(Guid cycleId, [FromQuery] string format = "print", CancellationToken ct = default)
    {
        var cycle = await db.PayrollCycles.AsNoTracking().SingleOrDefaultAsync(c => c.Id == cycleId, ct)
            ?? throw new DomainException("payroll.not_found", "Payroll cycle not found.");
        var lines = await db.PayrollLines.AsNoTracking().Where(l => l.PayrollCycleId == cycleId).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);
        var profiles = await db.EmployeeProfiles.AsNoTracking().ToDictionaryAsync(p => p.UserId, ct);
        var departments = await db.Departments.AsNoTracking().ToDictionaryAsync(d => d.Id, d => d.NameAr, ct);

        var rows = lines.Select(l =>
        {
            var user = users.GetValueOrDefault(l.EmployeeId);
            var profile = profiles.GetValueOrDefault(l.EmployeeId);
            return new[]
            {
                user?.EmployeeNumber ?? "—", user?.FullName ?? "—",
                profile is null ? "—" : departments.GetValueOrDefault(profile.DepartmentId, "—"),
                Money(l.BasicSalary), $"{l.PresentDays}/{l.ScheduledDays}",
                l.AbsentDays.ToString(CultureInfo.InvariantCulture), Money(l.Earnings),
                Money(l.CappedDeductions), Money(l.NetPay),
            };
        }).OrderBy(r => r[0], StringComparer.Ordinal).ToList();

        rows.Add(["", "الإجمالي", "", "", "", "", Money(lines.Sum(l => l.Earnings)),
            Money(lines.Sum(l => l.CappedDeductions)), Money(lines.Sum(l => l.NetPay))]);

        var headers = new[] { "الرقم الوظيفي", "الاسم", "الإدارة", "الراتب الأساسي", "الحضور/المجدول",
            "الغياب", "المستحقات", "الخصومات", "صافي الراتب" };

        return Render("مسيّر الرواتب", $"{cycle.Year}-{cycle.Month:D2} · {StatusArabic(cycle.Status)}",
            headers, rows, format, $"payroll_{cycle.Year}-{cycle.Month:D2}");
    }

    [HttpGet("deductions")]
    public async Task<IActionResult> Deductions([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] string format = "print", CancellationToken ct = default)
    {
        var proposals = await db.DeductionProposals.AsNoTracking()
            .Where(p => p.IsActive && p.OnDate >= from && p.OnDate <= to).OrderBy(p => p.OnDate).ToListAsync(ct);
        var types = await db.DeductionTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.NameAr, ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, ct);

        var rows = proposals.Select(p => new[]
        {
            p.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            users.GetValueOrDefault(p.EmployeeId)?.EmployeeNumber ?? "—",
            users.GetValueOrDefault(p.EmployeeId)?.FullName ?? "—",
            types.GetValueOrDefault(p.DeductionTypeId, "—"),
            p.Units.ToString("0.##", CultureInfo.InvariantCulture),
            p.ApprovedAmount is { } amount ? Money(amount) : "—",
            StatusArabic(p.Status), p.Reason,
        }).ToList();

        var headers = new[] { "التاريخ", "الرقم الوظيفي", "الاسم", "نوع الخصم", "المقدار", "القيمة المعتمدة", "الحالة", "السبب" };
        return Render("تقرير الخصومات والإنذارات", $"من {from} إلى {to}", headers, rows, format, $"deductions_{from}_{to}");
    }

    /// <summary>One renderer for every report: a print-ready page, or the same page as a Word file.</summary>
    private IActionResult Render(string title, string subtitle, IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows, string format, string fileName)
    {
        var html = BuildHtml(title, subtitle, headers, rows);
        if (!string.Equals(format, "word", StringComparison.OrdinalIgnoreCase))
            return Content(html, "text/html; charset=utf-8", Encoding.UTF8);

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(html)).ToArray();
        return File(bytes, "application/msword", $"{fileName}.doc");
    }

    private string BuildHtml(string title, string subtitle, IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        var html = new StringBuilder();
        html.Append("""
            <!doctype html><html lang="ar" dir="rtl"><head><meta charset="utf-8">
            <style>
              @page { size: A4 landscape; margin: 12mm; }
              body { font-family: "Sakkal Majalla", "Traditional Arabic", Tahoma, sans-serif; color: #14201f; }
              header { display: flex; align-items: center; gap: 12px; border-bottom: 2px solid #008540; padding-bottom: 8px; }
              header h1 { margin: 0; font-size: 18pt; color: #008540; }
              header .meta { margin-inline-start: auto; font-size: 9pt; color: #555; text-align: end; }
              h2 { margin: 4px 0 12px; font-size: 11pt; font-weight: normal; color: #555; }
              table { width: 100%; border-collapse: collapse; font-size: 9pt; }
              th, td { border: 1px solid #cfd8d5; padding: 5px 6px; text-align: right; }
              th { background: #e8f3ec; color: #14201f; }
              tr:nth-child(even) td { background: #fafbfa; }
              tr:last-child td { font-weight: bold; background: #e8f3ec; }
              footer { margin-top: 10px; font-size: 8pt; color: #777; }
              @media print { .no-print { display: none; } }
            </style></head><body>
            """);

        html.Append(CultureInfo.InvariantCulture, $"""
            <header>
              <div><h1>{Escape(title)}</h1><div style="font-size:10pt">جمعية الشارقة الخيرية</div></div>
              <div class="meta">تاريخ الإصدار: {clock.Now:yyyy-MM-dd HH:mm}<br>{Escape(User.Identity?.Name ?? "")}</div>
            </header>
            <h2>{Escape(subtitle)}</h2>
            <div class="no-print" style="margin:10px 0"><button onclick="window.print()">طباعة أو حفظ PDF</button></div>
            <table><thead><tr>
            """);

        foreach (var header in headers) html.Append(CultureInfo.InvariantCulture, $"<th>{Escape(header)}</th>");
        html.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var cell in row) html.Append(CultureInfo.InvariantCulture, $"<td>{Escape(cell)}</td>");
            html.Append("</tr>");
        }

        html.Append(CultureInfo.InvariantCulture, $"""
            </tbody></table>
            <footer>عدد السجلات: {rows.Count} — تقرير مستخرج من نظام الحضور والموارد البشرية</footer>
            </body></html>
            """);
        return html.ToString();
    }

    private static string Hours(int minutes) =>
        $"{minutes / 60}:{Math.Abs(minutes % 60):D2}";

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string StatusArabic(PayrollStatus status) => status switch
    {
        PayrollStatus.Draft => "مسودة",
        PayrollStatus.Review => "قيد المراجعة",
        PayrollStatus.Approved => "معتمدة",
        _ => "مقفلة",
    };

    private static string StatusArabic(DeductionStatus status) => status switch
    {
        DeductionStatus.Proposed => "مقترح",
        DeductionStatus.Approved => "معتمد",
        DeductionStatus.ConvertedToWarning => "حُوّل لإنذار",
        _ => "ملغى",
    };

    private static string Escape(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
