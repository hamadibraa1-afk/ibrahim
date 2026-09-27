using System.Globalization;
using FieldAttendance.Api.Auth;
using FieldAttendance.Api.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Geo;
using FieldAttendance.Domain.Scheduling;
using FieldAttendance.Domain.Time;
using Microsoft.EntityFrameworkCore;

namespace FieldAttendance.Api.Data;

/// <summary>
/// Demo data for local testing only (Development + SeedDemoData = true). Runs once on an empty database.
///
/// It builds an organisation complete enough to judge the reports: seven branches, eight
/// departments, office staff spread across them, and roughly a quarter of real attendance —
/// punctual days, late arrivals, absences, overtime — plus the warnings and deduction proposals
/// that come out of them.
///
/// Coordinates are approximate town centres across the Emirate of Sharjah: good enough for the
/// map and the geofence, not a survey. Point a branch at your own position before testing check-in.
/// </summary>
public static class DevSeeder
{
    public const string DemoPassword = "Test@1234";

    private const WorkDays SunToThu = WorkDays.Sunday | WorkDays.Monday | WorkDays.Tuesday | WorkDays.Wednesday | WorkDays.Thursday;
    private const WorkDays SatToWed = WorkDays.Saturday | WorkDays.Sunday | WorkDays.Monday | WorkDays.Tuesday | WorkDays.Wednesday;

    /// <summary>Fixed so a reset reproduces the same history and a figure stays where you left it.</summary>
    private const int HistorySeed = 20260924;

    public static async Task SeedAsync(AppDbContext db, IClock clock, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(clock);
        if (await db.Users.AnyAsync(ct)) return;

        var now = clock.Now;
        var today = clock.Today;
        var weekStart = today.AddDays(-(int)today.DayOfWeek);
        var hash = PasswordHasher.Hash(DemoPassword);

        var accounts = SeedAccounts(db, hash);
        var field = SeedFieldModule(db, now, today, weekStart, accounts);
        var office = SeedOrganisation(db, hash, now, weekStart, accounts);

        SeedPolicies(db, today);
        SeedDiscipline(db, out var warningLevels, out var deductionTypes);
        SeedApprovalFlows(db);
        SeedFieldRequests(db, field, accounts, today, now);
        SeedCustomerVoice(db, field, today, now);

        await db.SaveChangesAsync(ct);

        // History last: it needs the branches, schedules and people to exist first.
        // The HR manager signs the permissions and leave, as they would in practice.
        var history = DevSeederHistory.Build(office.Targets, field.LeaveTypes, accounts.HrManager.Id, today, now, HistorySeed);
        db.AttendanceRecords.AddRange(history.Records);
        db.PermissionRequests.AddRange(history.Permissions);
        db.LeaveRequests.AddRange(history.Leaves);
        db.LeaveBalances.AddRange(history.Balances);
        await db.SaveChangesAsync(ct);

        // Only unexcused days reach discipline: a permission that was approved costs nothing.
        SeedDisciplineHistory(db, history.Records, office, warningLevels, deductionTypes, accounts.Admin, now);
        await db.SaveChangesAsync(ct);
    }

    // ---------------------------------------------------------------- accounts

    private sealed record Accounts(User Admin, User Supervisor, User MosqueSupervisor, User Viewer,
        User HrManager, User HrOfficer, User[] Collectors);

    private static Accounts SeedAccounts(AppDbContext db, string hash)
    {
        var admin = new User("مدير النظام", "admin@test.local", "0500000000", UserRole.SystemAdmin, "1001", "ar");
        var supervisor = new User("سعيد المشرف", "sup@test.local", "0500000001", UserRole.Supervisor, "1002", "ar");
        var viewer = new User("خالد مدير الإدارة", "mgr@test.local", "0500000002", UserRole.DepartmentManager, "1003", "ar");
        var mosqueSupervisor = new User("جاسم مشرف المساجد", "sup2@test.local", "0500000003", UserRole.Supervisor, "1004", "ar");
        var hrManager = new User("سارة الحمادي", "hr@test.local", "0505550001", UserRole.HrManager, "1005", "ar");
        var hrOfficer = new User("أمل الزرعوني", "hr2@test.local", "0505550002", UserRole.HrOfficer, "1006", "ar");

        string[] collectorNames =
        [
            "علي أحمد المرزوقي", "عمر خالد الشامسي", "سارة يوسف النقبي", "محمد عبدالله البلوشي", "فاطمة راشد الحمادي",
            "خالد سعيد الكعبي", "نورة حسن الزعابي", "يوسف إبراهيم السويدي", "مريم علي الظاهري", "عبدالرحمن ماجد النعيمي",
            "هند سالم المهيري", "سلطان فيصل العامري", "لطيفة جمعة الشحي", "ناصر عادل الجنيبي", "أسماء طارق المازمي",
            "حمد مبارك الكتبي", "شيخة وليد الحوسني", "إبراهيم منصور الطنيجي", "موزة صقر السركال", "راشد بدر اليماحي",
        ];
        var collectors = collectorNames.Select((name, i) => new User(name, $"c{2001 + i}@test.local",
            $"05{11111111 + (i * 111111):D8}", UserRole.Collector, (2001 + i).ToString(CultureInfo.InvariantCulture), "ar")).ToArray();

        var all = new[] { admin, supervisor, viewer, mosqueSupervisor, hrManager, hrOfficer }.Concat(collectors).ToArray();
        foreach (var user in all) user.SetPasswordHash(hash);
        db.Users.AddRange(all);

        return new Accounts(admin, supervisor, mosqueSupervisor, viewer, hrManager, hrOfficer, collectors);
    }

    // ---------------------------------------------------------------- field module

    private sealed record FieldModule(Location[] Sites, ShiftTemplate Morning, ShiftTemplate Evening, LeaveType[] LeaveTypes);

    private static FieldModule SeedFieldModule(AppDbContext db, DateTimeOffset now, DateOnly today, DateOnly weekStart, Accounts accounts)
    {
        var cityCentre = new Location("سيتي سنتر الشارقة", "City Centre Sharjah", "شارع الوحدة، الشارقة", new GeoPoint(25.3247, 55.3925), 180, 2, now);
        var megaMall = new Location("ميجا مول الشارقة", "Mega Mall Sharjah", "شارع الملك عبدالعزيز، الشارقة", new GeoPoint(25.3307, 55.3893), 150, 2, now);
        var sahara = new Location("ساحة الصحراء", "Sahara Centre", "النهدة، الشارقة", new GeoPoint(25.2882, 55.3713), 160, 2, now);
        var ansar = new Location("أنصار مول", "Ansar Mall", "النهدة، الشارقة", new GeoPoint(25.2897, 55.3610), 140, 1, now);
        var safeer = new Location("سفير مول", "Safeer Mall", "القاسمية، الشارقة", new GeoPoint(25.3453, 55.4237), 140, 2, now);
        var zero6 = new Location("زيرو 6 مول", "Zero 6 Mall", "الزاهية، الشارقة", new GeoPoint(25.2977, 55.4623), 150, 2, now);
        var matajer = new Location("متاجر الجرينة", "Matajer Al Juraina", "الجرينة، الشارقة", new GeoPoint(25.3049, 55.4356), 120, 1, now);
        var kingFaisal = new Location("مسجد الملك فيصل", "King Faisal Mosque", "ساحة الاتحاد، الشارقة", new GeoPoint(25.3554, 55.3887), 160, 2, now);
        var alNoor = new Location("مسجد النور", "Al Noor Mosque", "بحيرة خالد، الشارقة", new GeoPoint(25.3336, 55.3829), 130, 1, now);
        var sharjahMosque = new Location("جامع الشارقة", "Sharjah Mosque", "الطي، الشارقة", new GeoPoint(25.2793, 55.5299), 200, 2, now);
        var sites = new[] { cityCentre, megaMall, sahara, ansar, safeer, zero6, matajer, kingFaisal, alNoor, sharjahMosque };
        db.AddRange(sites);

        var morning = new ShiftTemplate("الفترة الصباحية", "Morning", new TimeOnly(8, 0), new TimeOnly(16, 0), 30, 10, false, 45);
        var evening = new ShiftTemplate("الفترة المسائية", "Evening", new TimeOnly(16, 0), new TimeOnly(23, 0), 30, 10, false, 45);
        var partial = new ShiftTemplate("فترة جزئية", "Part time", new TimeOnly(9, 0), new TimeOnly(13, 0), 0, 10, false, 30);
        db.AddRange(morning, evening, partial);

        var leaveTypes = new[]
        {
            new LeaveType("إجازة سنوية", "Annual leave", 30),
            new LeaveType("إجازة مرضية", "Sick leave", 15),
            new LeaveType("إجازة بدون راتب", "Unpaid leave", null),
        };
        db.AddRange(leaveTypes);

        // Deliberate gaps: three sites and six collectors are left unscheduled so the
        // scheduling warnings and the "available" picker have something real to show.
        var c = accounts.Collectors;
        (User Employee, Location Site, ShiftTemplate Shift, WorkDays Days)[] plan =
        [
            (c[0], cityCentre, morning, SunToThu), (c[1], cityCentre, morning, SunToThu), (c[2], cityCentre, evening, SunToThu),
            (c[3], megaMall, morning, SunToThu), (c[13], megaMall, evening, SatToWed),
            (c[4], sahara, morning, SunToThu), (c[5], sahara, evening, SunToThu),
            (c[6], ansar, morning, SunToThu), (c[7], safeer, morning, SunToThu), (c[8], safeer, evening, SunToThu),
            (c[9], zero6, morning, SunToThu), (c[10], zero6, partial, SunToThu),
            (c[11], kingFaisal, morning, SatToWed), (c[12], kingFaisal, evening, SatToWed),
        ];
        foreach (var (employee, site, shift, days) in plan)
            db.Add(new Assignment(employee.Id, site.Id, shift.Id, weekStart, null, days, null));

        db.AddRange(
            new SupervisorLocation(accounts.Supervisor.Id, cityCentre.Id),
            new SupervisorLocation(accounts.Supervisor.Id, megaMall.Id),
            new SupervisorLocation(accounts.Supervisor.Id, sahara.Id),
            new SupervisorLocation(accounts.Supervisor.Id, ansar.Id),
            new SupervisorLocation(accounts.Supervisor.Id, safeer.Id),
            new SupervisorLocation(accounts.Supervisor.Id, zero6.Id),
            new SupervisorLocation(accounts.MosqueSupervisor.Id, kingFaisal.Id),
            new SupervisorLocation(accounts.MosqueSupervisor.Id, alNoor.Id),
            new SupervisorLocation(accounts.MosqueSupervisor.Id, sharjahMosque.Id));

        var transport = new AllowanceType("بدل مواصلات", "Transport allowance", 25m, null);
        var fieldAllowance = new AllowanceType("بدل عمل ميداني", "Field allowance", 40m, null);
        db.AddRange(transport, fieldAllowance, new AllowanceType("بدل وجبة", "Meal allowance", 15m, null));
        db.AddRange(
            new EmployeeAllowance(c[0].Id, transport.Id, weekStart, weekStart.AddDays(27), "شهر كامل"),
            new EmployeeAllowance(c[1].Id, fieldAllowance.Id, today, today.AddDays(13), "أسبوعان"),
            new EmployeeAllowance(c[4].Id, transport.Id, today, today.AddDays(6), null));

        return new FieldModule(sites, morning, evening, leaveTypes);
    }

    // ---------------------------------------------------------------- organisation

    private sealed record Office(Location[] Branches, Department[] Departments, EmployeeProfile[] Profiles,
        User[] Staff, HistoryTarget[] Targets);

    private static Office SeedOrganisation(AppDbContext db, string hash, DateTimeOffset now, DateOnly weekStart, Accounts accounts)
    {
        var branches = DevSeedData.Branches
            .Select(b => new Location(b.NameAr, b.NameEn, b.Address, new GeoPoint(b.Lat, b.Lng), b.Radius, 500, now, LocationKind.Office))
            .ToArray();
        db.AddRange(branches);

        var departments = DevSeedData.Departments.Select(d => new Department(d.NameAr, d.NameEn, branches[0].Id, null)).ToArray();
        db.AddRange(departments);

        var titles = DevSeedData.JobTitles.Select(t => new JobTitle(t, t)).ToArray();
        var grades = new[] { new Grade("الدرجة الأولى", "Grade 1"), new Grade("الدرجة الثانية", "Grade 2"), new Grade("الدرجة الثالثة", "Grade 3") };
        var contracts = new[] { new ContractType("دوام كامل", "Full time"), new ContractType("عقد مؤقت", "Fixed term") };
        db.AddRange(titles); db.AddRange(grades); db.AddRange(contracts);

        // Two patterns: the standard week, and a shorter Thursday that proves days may differ.
        var standard = new WorkSchedule("دوام إداري", "Office hours", 10, 30, false, flexMinutes: 30);
        foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday })
            standard.SetDay(day, new TimeOnly(8, 0), new TimeOnly(16, 0), 30);
        standard.SetDay(DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(14, 0), 0);

        var branchHours = new WorkSchedule("دوام الفروع", "Branch hours", 15, 30, false);
        foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday })
            branchHours.SetDay(day, new TimeOnly(7, 30), new TimeOnly(15, 30), 30);
        db.AddRange(standard, branchHours);

        // Office attendance runs through the field engine, so each pattern becomes a shift template.
        var officeShift = new ShiftTemplate("دوام إداري 08:00 - 16:00", "Office 08:00 - 16:00",
            new TimeOnly(8, 0), new TimeOnly(16, 0), 30, 10, false, 30);
        officeShift.SetFlex(standard.FlexMinutes);
        var branchShift = new ShiftTemplate("دوام الفروع 07:30 - 15:30", "Branch 07:30 - 15:30",
            new TimeOnly(7, 30), new TimeOnly(15, 30), 30, 15, false, 30);
        db.AddRange(officeShift, branchShift);

        var hire = weekStart.AddDays(-540);
        var staff = new List<User>();
        var profiles = new List<EmployeeProfile>();
        var targets = new List<HistoryTarget>();

        foreach (var (seed, index) in DevSeedData.Staff.Select((s, i) => (s, i)))
        {
            var number = (3001 + index).ToString(CultureInfo.InvariantCulture);
            var role = Enum.TryParse<UserRole>(seed.Role, out var parsed) ? parsed : UserRole.Employee;
            var user = new User(seed.Name, $"e{number}@test.local",
                $"050{3000000 + (index * 4321):D7}", role, number, "ar");
            user.SetPasswordHash(hash);
            staff.Add(user);

            var branch = branches[seed.Branch];
            var department = departments[seed.Department];
            var schedule = seed.Branch == 0 ? standard : branchHours;
            var shift = seed.Branch == 0 ? officeShift : branchShift;

            var profile = new EmployeeProfile(user.Id, branch.Id, department.Id, hire);
            profile.SetJob(titles.First(t => t.NameAr == seed.Title).Id, grades[index % grades.Length].Id, contracts[0].Id, hire);
            profile.SetSchedule(schedule.Id);
            profile.SetPersonal("الإمارات", null, null, null, null);
            profiles.Add(profile);

            if (seed.IsDepartmentManager) department.SetManager(user.Id);

            db.Add(profile.ChangeSalary(seed.Salary, hire, "الراتب عند التعيين", accounts.Admin.Id));
            db.Add(new Assignment(user.Id, branch.Id, shift.Id, weekStart.AddDays(-90), null,
                schedule.WorkingDays, null));

            targets.Add(new HistoryTarget(user.Id, branch, shift, schedule));
        }

        db.Users.AddRange(staff);

        // The HR accounts need records of their own: without a profile they would be missing from
        // the employee list, the payroll run and every report, while still approving other people's.
        var services = departments[5];
        foreach (var (user, salary) in new[] { (accounts.HrManager, 17500m), (accounts.HrOfficer, 11000m) })
        {
            var profile = new EmployeeProfile(user.Id, branches[0].Id, services.Id, hire);
            profile.SetJob(titles.First(t => t.NameAr == "أخصائي موارد بشرية").Id, grades[0].Id, contracts[0].Id, hire);
            profile.SetSchedule(standard.Id);
            profile.SetPersonal("الإمارات", null, null, null, null);
            profiles.Add(profile);

            db.Add(profile.ChangeSalary(salary, hire, "الراتب عند التعيين", accounts.Admin.Id));
            db.Add(new Assignment(user.Id, branches[0].Id, officeShift.Id, weekStart.AddDays(-90), null,
                standard.WorkingDays, null));
            targets.Add(new HistoryTarget(user.Id, branches[0], officeShift, standard));
        }

        db.AddRange(profiles);

        // Line managers: at head office the department manager, at a branch the branch lead.
        var branchLeads = new Dictionary<int, Guid>();
        var departmentManagers = new Dictionary<int, Guid>();
        for (var i = 0; i < DevSeedData.Staff.Length; i++)
        {
            var seed = DevSeedData.Staff[i];
            if (seed.IsBranchLead) branchLeads[seed.Branch] = staff[i].Id;
            if (seed.IsDepartmentManager) departmentManagers[seed.Department] = staff[i].Id;
        }

        for (var i = 0; i < DevSeedData.Staff.Length; i++)
        {
            var seed = DevSeedData.Staff[i];
            if (seed.IsDepartmentManager || seed.IsBranchLead) continue;

            var manager = seed.Branch == 0
                ? departmentManagers.TryGetValue(seed.Department, out var head) ? head : Guid.Empty
                : branchLeads.TryGetValue(seed.Branch, out var lead) ? lead : Guid.Empty;

            if (manager != Guid.Empty && manager != staff[i].Id)
                profiles[i].SetPlacement(branches[seed.Branch].Id, departments[seed.Department].Id, null, manager);
        }

        // Field operations belong to Fundraising. The supervisors are office staff of that department,
        // under its manager; each collector is field staff reporting to the supervisor of their site.
        // That chain is what their requests travel: supervisor first, then the department manager.
        var fundraising = departments[7];
        var fundraisingManager = departmentManagers[7];
        var supervisorTitle = titles.First(t => t.NameAr == "مشرف ميداني").Id;
        foreach (var (user, salary) in new[] { (accounts.Supervisor, 12000m), (accounts.MosqueSupervisor, 11500m) })
        {
            var profile = new EmployeeProfile(user.Id, branches[0].Id, fundraising.Id, hire);
            profile.SetPlacement(branches[0].Id, fundraising.Id, null, fundraisingManager);
            profile.SetJob(supervisorTitle, grades[1].Id, contracts[0].Id, hire);
            profile.SetSchedule(standard.Id);
            profile.SetPersonal("الإمارات", null, null, null, null);
            db.Add(profile);
            db.Add(profile.ChangeSalary(salary, hire, "الراتب عند التعيين", accounts.Admin.Id));
            db.Add(new Assignment(user.Id, branches[0].Id, officeShift.Id, weekStart.AddDays(-90), null, standard.WorkingDays, null));
            targets.Add(new HistoryTarget(user.Id, branches[0], officeShift, standard));
        }

        // Every account is an employee whatever its permissions: the system administrator works in IT,
        // the read-only reviewer in Support Services. Each checks in and has a profile like anyone else.
        foreach (var (user, department, title, salary) in new[]
                 {
                     (accounts.Admin, 0, "مطور أنظمة", 16000m),
                     (accounts.Viewer, 5, "رئيس قسم", 15000m),
                 })
        {
            var profile = new EmployeeProfile(user.Id, branches[0].Id, departments[department].Id, hire);
            profile.SetPlacement(branches[0].Id, departments[department].Id, null, departmentManagers[department]);
            profile.SetJob(titles.First(t => t.NameAr == title).Id, grades[0].Id, contracts[0].Id, hire);
            profile.SetSchedule(standard.Id);
            profile.SetPersonal("الإمارات", null, null, null, null);
            db.Add(profile);
            db.Add(profile.ChangeSalary(salary, hire, "الراتب عند التعيين", accounts.Admin.Id));
            db.Add(new Assignment(user.Id, branches[0].Id, officeShift.Id, weekStart.AddDays(-90), null, standard.WorkingDays, null));
            targets.Add(new HistoryTarget(user.Id, branches[0], officeShift, standard));
        }

        var collectorTitle = titles.First(t => t.NameAr == "محصّل ميداني").Id;
        for (var i = 0; i < accounts.Collectors.Length; i++)
        {
            var collector = accounts.Collectors[i];
            // Collectors 12 and 13 work the King Faisal mosque site, which the mosque supervisor runs.
            var supervisor = i is 11 or 12 ? accounts.MosqueSupervisor : accounts.Supervisor;
            var profile = new EmployeeProfile(collector.Id, branches[0].Id, fundraising.Id, hire, Workforce.Field);
            profile.SetPlacement(branches[0].Id, fundraising.Id, null, supervisor.Id);
            profile.SetJob(collectorTitle, grades[2].Id, contracts[i % 2].Id, hire);
            profile.SetPersonal("الإمارات", null, null, null, null);
            db.Add(profile);
            db.Add(profile.ChangeSalary(4200m + (i % 5 * 300m), hire, "الراتب عند التعيين", accounts.Admin.Id));
        }

        // Expiring documents, so the HR reminder list is not empty on day one.
        profiles[9].SetDocuments("784198500123456", weekStart.AddDays(25), "P4521889", weekStart.AddDays(70), null, weekStart.AddDays(18), "AE070331234567890123456");
        profiles[14].SetDocuments("784199100654321", weekStart.AddDays(120), "N8812355", weekStart.AddDays(32), null, null, "AE120331234567890999888");

        return new Office(branches, departments, [.. profiles], [.. staff], [.. targets]);
    }

    // ---------------------------------------------------------------- settings and policy

    private static void SeedPolicies(AppDbContext db, DateOnly today)
    {
        db.AddRange(
            new SystemSetting("payroll.monthDays", "30"),
            new SystemSetting("payroll.dailyWorkMinutes", "480"),
            new SystemSetting("payroll.overtimeFactor", "1.25"),
            new SystemSetting("payroll.maxDeductionPercent", "25"));

        db.AddRange(
            new Holiday("اليوم الوطني", "National Day", new DateOnly(today.Year, 12, 2), new DateOnly(today.Year, 12, 3)),
            new Holiday("رأس السنة الهجرية", "Islamic New Year", today.AddDays(45), today.AddDays(45)));
    }

    private static void SeedDiscipline(AppDbContext db, out WarningLevel[] levels, out DeductionType[] types)
    {
        var verbal = new WarningLevel("تنبيه شفهي موثّق", "Verbal note", 1, 90, "نلفت انتباهكم إلى ملاحظة على الالتزام بمواعيد العمل.");
        var first = new WarningLevel("إنذار أول", "First warning", 2, 180, "يُوجَّه إليكم إنذار أول لتكرار المخالفة.");
        var final = new WarningLevel("إنذار نهائي", "Final warning", 3, 365, "يُوجَّه إليكم إنذار نهائي.");
        levels = [verbal, first, final];
        db.AddRange(levels);

        types =
        [
            new DeductionType("خصم تأخير", "Late deduction", DeductionUnit.Hour, 1m, DeductionTrigger.Late, 3, verbal.Id, 10m),
            new DeductionType("خصم غياب", "Absence deduction", DeductionUnit.Day, 1m, DeductionTrigger.Absence, 1, first.Id, 25m),
            new DeductionType("خصم انصراف مبكر", "Early departure", DeductionUnit.Hour, 1m, DeductionTrigger.EarlyDeparture, 2, verbal.Id, 10m),
            new DeductionType("مخالفة إدارية", "Administrative penalty", DeductionUnit.Day, 1m, DeductionTrigger.Manual, null, first.Id, 25m),
        ];
        db.AddRange(types);
    }

    private static void SeedApprovalFlows(AppDbContext db)
    {
        var leaveFlow = new ApprovalFlow(RequestKind.Leave, "مسار اعتماد الإجازات", "Leave approval", 48);
        leaveFlow.SetLevels([ApprovalStage.SectionHead, ApprovalStage.DepartmentManager, ApprovalStage.Hr]);
        var permissionFlow = new ApprovalFlow(RequestKind.Permission, "مسار اعتماد الأذونات", "Permission approval", 8);
        permissionFlow.SetLevels([ApprovalStage.LineManager]);

        // Field staff: their supervisor first, then the supervisor's department manager.
        var fieldLeave = new ApprovalFlow(RequestKind.Leave, "مسار إجازات الميدان", "Field leave approval", 48, Workforce.Field);
        fieldLeave.SetLevels([ApprovalStage.LineManager, ApprovalStage.DepartmentManager]);
        var fieldPermission = new ApprovalFlow(RequestKind.Permission, "مسار أذونات الميدان", "Field permission approval", 8, Workforce.Field);
        fieldPermission.SetLevels([ApprovalStage.LineManager, ApprovalStage.DepartmentManager]);
        db.AddRange(leaveFlow, permissionFlow, fieldLeave, fieldPermission);
    }

    // ---------------------------------------------------------------- requests and customer voice

    private static void SeedFieldRequests(AppDbContext db, FieldModule field, Accounts accounts, DateOnly today, DateTimeOffset now)
    {
        var c = accounts.Collectors;
        var supervisor = accounts.Supervisor;

        var morningWindow = ShiftTiming.Window(today, field.Morning.StartTime, field.Morning.EndTime);
        var eveningWindow = ShiftTiming.Window(today, field.Evening.StartTime, field.Evening.EndTime);

        var exitPermission = PermissionRequest.TemporaryExit(c[1].Id, today, new TimeOnly(11, 0), new TimeOnly(12, 0), morningWindow, "إنهاء معاملة حكومية", c[1].Id);
        exitPermission.Approve(supervisor.Id, now);

        db.AddRange(
            PermissionRequest.Late(c[0].Id, today, new TimeOnly(9, 30), morningWindow, "مراجعة طبية في المستشفى", c[0].Id),
            PermissionRequest.EarlyDeparture(c[4].Id, today, new TimeOnly(14, 0), morningWindow, "ظرف عائلي", c[4].Id),
            exitPermission,
            PermissionRequest.Late(c[2].Id, today, new TimeOnly(17, 0), eveningWindow, "ازدحام مروري", c[2].Id));

        var annual = field.LeaveTypes[0];
        var approvedDays = Math.Max(1, WorkingDays(today, today.AddDays(1), SunToThu));
        var approvedLeave = new LeaveRequest(c[6].Id, annual.Id, today, today.AddDays(1), approvedDays, "إجازة اضطرارية", c[6].Id);
        approvedLeave.Approve(supervisor.Id, now);
        var balance = new LeaveBalance(c[6].Id, annual.Id, today.Year, annual.AnnualBalanceDays);
        balance.Deduct(approvedDays);

        db.AddRange(
            new LeaveRequest(c[3].Id, annual.Id, today.AddDays(3), today.AddDays(7),
                WorkingDays(today.AddDays(3), today.AddDays(7), SunToThu), "سفر عائلي", c[3].Id),
            approvedLeave, balance);
    }

    private static void SeedCustomerVoice(AppDbContext db, FieldModule field, DateOnly today, DateTimeOffset now) =>
        db.AddRange(
            new Domain.Entities.Feedback(Domain.Entities.Feedback.BuildReference(today.Year, 1), FeedbackKind.Complaint,
                field.Sites[0].Id, null, null, "أحمد المري", "0551234567", "ahmed@example.com",
                "انتظرت وقتًا طويلًا عند نقطة التحصيل، أرجو زيادة عدد الموظفين وقت الذروة.", now.AddHours(-3)),
            new Domain.Entities.Feedback(Domain.Entities.Feedback.BuildReference(today.Year, 2), FeedbackKind.Suggestion,
                field.Sites[7].Id, null, null, "موزة الشامسي", "0559876543", null,
                "أقترح توفير لافتة إرشادية لمكان التبرع قرب المدخل الرئيسي.", now.AddHours(-20)));

    // ---------------------------------------------------------------- discipline from real history

    /// <summary>
    /// Turns the generated history into the discipline trail an HR officer would actually find:
    /// proposals raised from repeated lateness and absence, a few already decided, and warnings
    /// issued instead of money. Built from the records themselves so every figure is traceable.
    /// </summary>
    private static void SeedDisciplineHistory(AppDbContext db, IReadOnlyList<AttendanceRecord> records,
        Office office, WarningLevel[] levels, DeductionType[] types, User decidedBy, DateTimeOffset now)
    {
        var lateType = Array.Find(types, t => t.Trigger == DeductionTrigger.Late);
        var absenceType = Array.Find(types, t => t.Trigger == DeductionTrigger.Absence);
        if (lateType is null || absenceType is null) return;

        var random = new Random(HistorySeed + 1);
        var salaries = office.Profiles.ToDictionary(p => p.UserId, p => p.BasicSalary);
        var raised = 0;

        // The worst days first: a report opened on day one should show something worth deciding.
        var candidates = records
            .Where(r => r.LateUnexcused >= 20 || r.Status == AttendanceStatus.Absent)
            .OrderByDescending(r => r.ShiftDate)
            .Take(60)
            .ToList();

        foreach (var record in candidates)
        {
            var isAbsence = record.Status == AttendanceStatus.Absent;
            var type = isAbsence ? absenceType : lateType;
            var units = isAbsence ? 1m : decimal.Round(record.LateUnexcused / 60m, 2);
            if (units <= 0) continue;

            var reason = isAbsence ? "غياب بدون إذن" : $"تأخير غير مبرر {record.LateUnexcused} دقيقة";
            var proposal = new DeductionProposal(record.EmployeeId, type.Id, record.ShiftDate, units, reason, record.Id, null);

            // A third stay open for the reviewer, a third become money, a third become a warning.
            var outcome = raised % 3;
            if (outcome == 1)
            {
                var salary = salaries.GetValueOrDefault(record.EmployeeId);
                var amount = Domain.Payroll.PayrollMath.DeductionAmount(salary,
                    Domain.Payroll.PayrollPolicy.Default, type.Unit, units);
                proposal.Approve(decidedBy.Id, now.AddDays(-random.Next(1, 20)), amount, null, "اعتماد بعد مراجعة السجل");
            }
            else if (outcome == 2)
            {
                var level = levels[Math.Min(raised / 12, levels.Length - 1)];
                var warning = new Warning(record.EmployeeId, level.Id, reason, decidedBy.Id,
                    now.AddDays(-random.Next(1, 25)), record.ShiftDate);
                if (raised % 9 == 2) warning.FileObjection("كنت في مهمة رسمية خارج المقر", now.AddDays(-1));
                db.Warnings.Add(warning);
                proposal.ConvertToWarning(decidedBy.Id, now.AddDays(-random.Next(1, 20)), warning.Id, "إنذار بدل الخصم");
            }

            db.DeductionProposals.Add(proposal);
            raised++;
        }
    }

    private static int WorkingDays(DateOnly from, DateOnly to, WorkDays days)
    {
        var count = 0;
        for (var d = from; d <= to; d = d.AddDays(1))
            if (days.Includes(d.DayOfWeek)) count++;
        return count;
    }
}
