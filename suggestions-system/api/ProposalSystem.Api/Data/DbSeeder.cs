using Microsoft.EntityFrameworkCore;
using ProposalSystem.Api.Domain;
using ProposalSystem.Api.Security;

namespace ProposalSystem.Api.Data;

public static class DbSeeder
{
    public const string DemoPassword = "Passw0rd!";

    /// <summary>
    /// Form fields the system needs, added by key so it is safe on every start and on existing
    /// databases (an upgraded database gets the new ImpactMeasurement fields this way).
    /// </summary>
    public static async Task EnsureFormFieldsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = await db.FormFields.Select(f => f.FieldKey).ToListAsync(ct);
        var keys = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var order = existing.Count;

        void Add(string key, string label, FormFieldType type, string section, bool system, bool required = false, string? placeholder = null)
        {
            if (keys.Add(key))
                db.FormFields.Add(new FormField
                {
                    FieldKey = key, LabelAr = label, FieldType = type, Section = section, IsSystem = system,
                    IsRequired = required, IsActive = true, SortOrder = order++, Placeholder = placeholder,
                });
        }

        // Core submission fields (dedicated columns on Proposal).
        Add("title", "عنوان المقترح", FormFieldType.Text, FormSections.SuggestionData, true, true);
        Add("implementationMechanism", "آلية تطبيق المقترح", FormFieldType.TextArea, FormSections.SuggestionDetails, true, true);
        Add("submissionReasons", "أسباب تقديم المقترح", FormFieldType.TextArea, FormSections.SuggestionDetails, true, true);

        // The six impact boxes of SCI-M-02-F-01 (administrator-editable).
        Add("impactQuality", "تحسين جودة الخدمات", FormFieldType.Checkbox, FormSections.Impact, false);
        Add("impactCost", "خفض التكاليف", FormFieldType.Checkbox, FormSections.Impact, false);
        Add("impactTime", "توفير الوقت والجهد", FormFieldType.Checkbox, FormSections.Impact, false);
        Add("impactSatisfaction", "رفع رضا المتعاملين والمستفيدين", FormFieldType.Checkbox, FormSections.Impact, false);
        Add("impactRevenue", "زيادة الإيرادات والتبرعات", FormFieldType.Checkbox, FormSections.Impact, false);
        Add("impactReputation", "تعزيز السمعة المؤسسية", FormFieldType.Checkbox, FormSections.Impact, false);

        // Post-implementation "actual impact" fields (dedicated columns on ImpactAssessment).
        const string m = FormSections.ImpactMeasurement;
        Add("actualAnnualSavings", "الوفورات المالية السنوية الفعلية (درهم)", FormFieldType.Number, m, true, true);
        Add("actualAnnualRevenue", "الإيرادات/التبرعات الإضافية السنوية (درهم)", FormFieldType.Number, m, true);
        Add("implementationCost", "تكلفة التطبيق الفعلية (درهم)", FormFieldType.Number, m, true, true);
        Add("hoursSavedPerMonth", "ساعات العمل الموفّرة شهرياً", FormFieldType.Number, m, true);
        Add("beneficiariesReached", "عدد المستفيدين الإضافيين", FormFieldType.Number, m, true);
        Add("satisfactionBefore", "نسبة الرضا قبل التطبيق (%)", FormFieldType.Number, m, true);
        Add("satisfactionAfter", "نسبة الرضا بعد التطبيق (%)", FormFieldType.Number, m, true);
        Add("targetAchievementPercent", "نسبة تحقق المستهدف (%)", FormFieldType.Number, m, true, true);
        Add("impactRating", "تقييم الأثر المؤسسي (1–5)", FormFieldType.Number, m, true, true);
        Add("impactSummary", "وصف الأثر الفعلي والدروس المستفادة", FormFieldType.TextArea, m, true, true);
        Add("evidenceReference", "مرجع الأدلة (تقرير، رابط، رقم معاملة)", FormFieldType.Text, m, true);

        await db.SaveChangesAsync(ct);
    }

    /// <summary>Demo accounts for a fresh development database. Never runs outside Development.</summary>
    public static async Task SeedDemoUsersAsync(AppDbContext db, TimeProvider clock, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct))
            return;
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = PasswordHasher.Hash(DemoPassword);

        User Make(string code, string ar, string en, UserRole role, string dept, string title) => new()
        {
            UserCode = code, ArabicName = ar, EnglishName = en, Email = $"{code.ToLowerInvariant()}@shjcharity.ae",
            Role = role, Department = dept, JobTitle = title, PhoneNumber = "06-0000000", PasswordHash = hash, CreatedAt = now,
        };

        var admin = Make("EMP-1001", "إبراهيم عبدالجليل حمد", "Ibrahim Abduljalil Hamad", UserRole.Admin, "إدارة التميز المؤسسي", "مدير النظام");
        var manager = Make("EMP-1004", "مريم الشامسي", "Maryam Alshamsi", UserRole.Employee, "إدارة التميز المؤسسي", "مدير إدارة التميز المؤسسي");
        db.Users.AddRange(admin, manager);
        await db.SaveChangesAsync(ct);

        var screener = Make("EMP-1002", "سارة المنصوري", "Sara Almansoori", UserRole.Screener, "إدارة التميز المؤسسي", "أخصائي فرز المقترحات");
        var committee = Make("EMP-1003", "قاسم الذوخي", "Qasim Althokhi", UserRole.CommitteeMember, "الإدارة المالية", "عضو لجنة دراسة المقترحات");
        var employee = Make("EMP-2001", "أحمد سالم", "Ahmed Salem", UserRole.Employee, "إدارة المشاريع الخارجية", "منسق مشاريع");
        // Reporting line: the escalation path when the screener or the committee is late.
        screener.ManagerId = manager.Id;
        committee.ManagerId = manager.Id;
        employee.ManagerId = manager.Id;
        manager.ManagerId = admin.Id;
        db.Users.AddRange(screener, committee, employee);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// "dotnet run --seed-passwords": gives every account without a valid PBKDF2 hash the demo
    /// password (SQL scripts cannot produce PBKDF2 hashes).
    /// </summary>
    public static async Task<int> SeedPasswordsAsync(AppDbContext db, CancellationToken ct = default)
    {
        var users = await db.Users.ToListAsync(ct);
        var fixedCount = 0;
        foreach (var u in users.Where(u => !PasswordHasher.IsValidHash(u.PasswordHash)))
        {
            u.PasswordHash = PasswordHasher.Hash(DemoPassword);
            fixedCount++;
        }
        await db.SaveChangesAsync(ct);
        return fixedCount;
    }
}
