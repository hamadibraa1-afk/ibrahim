namespace FieldAttendance.Api.Data;

/// <param name="Radius">Verification radius in metres; a small office needs less than a campus.</param>
internal sealed record BranchSeed(string NameAr, string NameEn, string Address, double Lat, double Lng, int Radius);

internal sealed record DepartmentSeed(string NameAr, string NameEn);

/// <param name="Branch">Index into <see cref="DevSeedData.Branches"/>.</param>
/// <param name="Department">Index into <see cref="DevSeedData.Departments"/>.</param>
/// <param name="Role">Office role; the first person in a department becomes its manager.</param>
internal sealed record StaffSeed(string Name, int Branch, int Department, string Title, decimal Salary,
    string Role = "Employee", bool IsDepartmentManager = false, bool IsBranchLead = false);

/// <summary>
/// The tables behind the demo organisation. Kept apart from the seeding logic so the data can be
/// read, corrected or extended without touching the code that writes it.
///
/// Coordinates are approximate town centres across the Emirate of Sharjah — close enough to see
/// each branch on the map, not a survey. Set a branch to your own position before testing check-in.
/// </summary>
internal static class DevSeedData
{
    public static readonly BranchSeed[] Branches =
    [
        new("المقر الرئيسي", "Head Office", "شارع الوحدة، الشارقة", 25.3462, 55.4211, 180),
        new("مقر الذيد", "Al Dhaid Office", "الذيد، الشارقة", 25.2881, 55.8814, 150),
        new("مقر المدام", "Al Madam Office", "المدام، الشارقة", 24.9264, 55.7869, 150),
        new("مقر البطايح", "Al Bataeh Office", "البطايح، الشارقة", 25.2469, 55.7061, 150),
        new("مقر كلباء", "Kalba Office", "كلباء، الشارقة", 25.0500, 56.3475, 150),
        new("مقر خورفكان", "Khorfakkan Office", "خورفكان، الشارقة", 25.3389, 56.3564, 150),
        new("مقر دبا الحصن", "Dibba Al-Hisn Office", "دبا الحصن، الشارقة", 25.6206, 56.2722, 150),
    ];

    public static readonly DepartmentSeed[] Departments =
    [
        new("إدارة تقنية المعلومات والتحول الذكي", "IT & Digital Transformation"),
        new("إدارة المساعدات", "Aid & Assistance"),
        new("إدارة المشاريع", "Projects"),
        new("إدارة الأيتام", "Orphans Care"),
        new("إدارة العلاقات العامة والاتصال المؤسسي", "PR & Corporate Communication"),
        new("إدارة الخدمات", "Support Services"),
        new("إدارة التطوع", "Volunteering"),
        new("إدارة جمع التبرعات", "Fundraising"),
    ];

    public static readonly string[] JobTitles =
    [
        "مدير إدارة", "رئيس قسم", "أخصائي أول", "أخصائي", "منسق", "باحث اجتماعي",
        "مهندس شبكات", "مطور أنظمة", "محاسب", "أخصائي موارد بشرية", "إعلامي", "مسؤول فرع",
        "مشرف ميداني", "محصّل ميداني",
    ];

    /// <summary>
    /// The office staff. Department managers sit at head office; branches carry the field-facing
    /// departments (aid, orphans, fundraising, volunteering) because that is where beneficiaries are.
    /// </summary>
    public static readonly StaffSeed[] Staff =
    [
        // ---- Head office: the eight department managers ----
        new("عبدالله سالم الحوسني", 0, 0, "مدير إدارة", 19000m, "DepartmentManager", IsDepartmentManager: true),
        new("محمد راشد الشامسي", 0, 1, "مدير إدارة", 18500m, "DepartmentManager", IsDepartmentManager: true),
        new("خالد عبيد النقبي", 0, 2, "مدير إدارة", 18500m, "DepartmentManager", IsDepartmentManager: true),
        new("منى حسن الزعابي", 0, 3, "مدير إدارة", 18000m, "DepartmentManager", IsDepartmentManager: true),
        new("أحمد يوسف المرزوقي", 0, 4, "مدير إدارة", 18000m, "DepartmentManager", IsDepartmentManager: true),
        new("سيف ماجد الكعبي", 0, 5, "مدير إدارة", 17500m, "DepartmentManager", IsDepartmentManager: true),
        new("عائشة علي الظاهري", 0, 6, "مدير إدارة", 17000m, "DepartmentManager", IsDepartmentManager: true),
        new("ناصر جمعة السويدي", 0, 7, "مدير إدارة", 18500m, "DepartmentManager", IsDepartmentManager: true),

        // ---- Head office: IT and digital transformation ----
        new("طارق منصور المنصوري", 0, 0, "مهندس شبكات", 13000m),
        new("هدى إبراهيم البلوشي", 0, 0, "مطور أنظمة", 12500m),
        new("راشد بدر اليماحي", 0, 0, "أخصائي أول", 11500m),
        new("ليلى عادل الجنيبي", 0, 0, "منسق", 9000m),

        // ---- Head office: aid, projects, PR, services, fundraising ----
        new("فاطمة سعيد الحمادي", 0, 1, "أخصائي أول", 12000m),
        new("عمر صقر السركال", 0, 1, "باحث اجتماعي", 10500m),
        new("مريم طارق المازمي", 0, 2, "أخصائي أول", 12500m),
        new("سلطان فيصل العامري", 0, 2, "منسق", 9500m),
        new("نورة وليد الشحي", 0, 4, "إعلامي", 11000m),
        new("يوسف حمد الكتبي", 0, 4, "منسق", 9000m),
        new("أسماء مبارك المهيري", 0, 5, "محاسب", 12000m),
        new("جاسم علي الطنيجي", 0, 5, "أخصائي", 10000m),
        new("لطيفة خالد النعيمي", 0, 7, "أخصائي أول", 12500m),
        new("حمد إبراهيم الحوسني", 0, 7, "منسق", 9500m),
        new("موزة ناصر الشامسي", 0, 3, "باحث اجتماعي", 11000m),
        new("علي عبدالرحمن البلوشي", 0, 6, "منسق", 9000m),

        // ---- Al Dhaid ----
        new("سالم محمد الكعبي", 1, 1, "مسؤول فرع", 14000m, IsBranchLead: true),
        new("شيخة راشد النقبي", 1, 1, "باحث اجتماعي", 10000m),
        new("عبدالعزيز سيف الظاهري", 1, 3, "أخصائي", 10500m),
        new("هند عبيد المرزوقي", 1, 7, "منسق", 9000m),

        // ---- Al Madam ----
        new("مبارك حميد العامري", 2, 1, "مسؤول فرع", 13500m, IsBranchLead: true),
        new("ريم سلطان الجنيبي", 2, 3, "باحث اجتماعي", 10000m),
        new("فيصل أحمد اليماحي", 2, 6, "منسق", 8800m),

        // ---- Al Bataeh ----
        new("خميس علي الشحي", 3, 1, "مسؤول فرع", 13500m, IsBranchLead: true),
        new("العنود ماجد الكتبي", 3, 4, "إعلامي", 10000m),
        new("سعيد جمعة المازمي", 3, 7, "منسق", 9200m),

        // ---- Kalba ----
        new("حسن عبدالله الطنيجي", 4, 1, "مسؤول فرع", 14000m, IsBranchLead: true),
        new("جواهر يوسف النعيمي", 4, 3, "باحث اجتماعي", 10500m),
        new("مروان سالم السويدي", 4, 2, "أخصائي", 11000m),
        new("بدرية حمد الزعابي", 4, 6, "منسق", 8800m),

        // ---- Khorfakkan ----
        new("إبراهيم راشد الشحي", 5, 1, "مسؤول فرع", 14000m, IsBranchLead: true),
        new("سمية عادل الحمادي", 5, 3, "أخصائي", 10500m),
        new("عبدالله خميس اليماحي", 5, 7, "منسق", 9200m),
        new("وفاء منصور السركال", 5, 6, "منسق", 8800m),

        // ---- Dibba Al-Hisn ----
        new("سيف حمدان النقبي", 6, 1, "مسؤول فرع", 13500m, IsBranchLead: true),
        new("خلود عمر المهيري", 6, 3, "باحث اجتماعي", 10000m),
        new("راشد سعيد الكعبي", 6, 2, "منسق", 9500m),
    ];
}
