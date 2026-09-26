using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Entities;
using FieldAttendance.Domain.Enums;
using FieldAttendance.Domain.Ratings;
using FieldAttendance.Domain.Scheduling;
using Xunit;
using static FieldAttendance.Domain.Tests.TestTime;

namespace FieldAttendance.Domain.Tests;

public class RatingTests
{
    private static readonly PresentEmployee Ali = new(Guid.NewGuid(), Guid.NewGuid(), "Ali");
    private static readonly PresentEmployee Omar = new(Guid.NewGuid(), Guid.NewGuid(), "Omar");

    [Fact]
    public void Case12_OnePresent_LinksAutomatically()
    {
        var link = RatingLinkPolicy.Decide([Ali], null, withinShiftHours: true);
        Assert.Equal(Ali.EmployeeId, link.EmployeeId);
        Assert.Equal(RatingLinkType.Auto, link.LinkType);
    }

    [Fact]
    public void Case13_TwoPresent_CustomerMustChoose()
    {
        Assert.Throws<DomainException>(() => RatingLinkPolicy.Decide([Ali, Omar], null, true));
        Assert.Throws<DomainException>(() => RatingLinkPolicy.Decide([Ali, Omar], Guid.NewGuid(), true));
        var link = RatingLinkPolicy.Decide([Ali, Omar], Omar.EmployeeId, true);
        Assert.Equal(Omar.EmployeeId, link.EmployeeId);
        Assert.Equal(RatingLinkType.CustomerSelected, link.LinkType);
    }

    [Fact]
    public void Case14_NobodyPresent_LinksToLocationOnly()
    {
        var link = RatingLinkPolicy.Decide([], null, true);
        Assert.Null(link.EmployeeId);
        Assert.Equal(RatingLinkType.NoEmployee, link.LinkType);
    }

    [Fact]
    public void OutsideShiftHours_IsFlaggedAndExcludedFromAverage()
    {
        var link = RatingLinkPolicy.Decide([], null, false);
        Assert.Equal(RatingLinkType.OutsideShift, link.LinkType);
        var rating = new Rating(Guid.NewGuid(), null, null, 4, null, T(23, 0), "dev", "ip", link.LinkType);
        Assert.False(rating.IncludedInAverage);
    }

    [Fact]
    public void Stars_MustBeOneToFive() =>
        Assert.Throws<DomainException>(() => new Rating(Guid.NewGuid(), null, null, 6, null, T(9, 0), "dev", "ip", RatingLinkType.Auto));
}

public class FeedbackTests
{
    private static Feedback New(FeedbackKind kind = FeedbackKind.Complaint) =>
        new(Feedback.BuildReference(2026, 42), kind, Guid.NewGuid(), null, null,
            "أحمد المري", "0501234567", null, "الخدمة كانت بطيئة اليوم", T(10, 0));

    [Fact]
    public void Reference_IsHumanReadable() => Assert.Equal("SCI-2026-00042", Feedback.BuildReference(2026, 42));

    [Fact]
    public void Phone_IsNormalized() => Assert.Equal("+971501234567", New().CustomerPhone);

    [Fact]
    public void Workflow_RunsFromNewToClosed()
    {
        var f = New();
        var supervisor = Guid.NewGuid();
        Assert.Equal(FeedbackStatus.New, f.Status);
        f.Assign(supervisor);
        Assert.Equal(FeedbackStatus.InProgress, f.Status);
        f.Close(supervisor, "تم التواصل مع المتعامل ومعالجة الملاحظة", T(11, 0));
        Assert.Equal(FeedbackStatus.Closed, f.Status);
        Assert.Equal(60, f.HandlingMinutes);
    }

    [Fact]
    public void ClosedItem_CannotBeChangedUntilReopened()
    {
        var f = New(FeedbackKind.Suggestion);
        var user = Guid.NewGuid();
        f.Close(user, "شكرًا للاقتراح", T(11, 0));
        Assert.Throws<DomainException>(() => f.Escalate(user, null));
        f.Reopen();
        f.Escalate(user, "يحتاج قرار الإدارة");
        Assert.Equal(FeedbackStatus.Escalated, f.Status);
    }

    [Fact]
    public void Closing_RequiresAResolutionNote() =>
        Assert.Throws<DomainException>(() => New().Close(Guid.NewGuid(), " ", T(11, 0)));
}

public class OfficeScheduleTests
{
    private static WorkSchedule Standard()
    {
        var s = new WorkSchedule("دوام إداري", "Office", 10, 30, false);
        foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday })
            s.SetDay(day, new TimeOnly(8, 0), new TimeOnly(16, 0), 30);
        return s;
    }

    [Fact]
    public void IdenticalDays_BecomeOneAssignment()
    {
        var plan = Assert.Single(OfficeSchedulePlanner.Plan(Standard()));
        Assert.Equal(new TimeOnly(8, 0), plan.Start);
        Assert.True(plan.Days.Includes(DayOfWeek.Sunday));
        Assert.False(plan.Days.Includes(DayOfWeek.Friday));
    }

    [Fact]
    public void ShorterDay_BecomesItsOwnAssignment()
    {
        var s = Standard();
        s.SetDay(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(12, 0), 0);
        Assert.Equal(2, OfficeSchedulePlanner.Plan(s).Count);
    }

    [Fact]
    public void ChangingADay_ReplacesItInsteadOfDuplicating()
    {
        var s = Standard();
        s.SetDay(DayOfWeek.Sunday, new TimeOnly(9, 0), new TimeOnly(17, 0), 30);
        Assert.Equal(5, s.Days.Count);
        Assert.Equal(new TimeOnly(9, 0), s.For(DayOfWeek.Sunday)!.StartTime);
    }

    [Fact]
    public void WeeklyMinutes_ExcludeBreaks() => Assert.Equal(5 * 450, Standard().WeeklyMinutes);

    [Fact]
    public void EmptySchedule_CannotBePlanned() =>
        Assert.Throws<DomainException>(() => OfficeSchedulePlanner.Plan(new WorkSchedule("x", "x", 0, 0, false)));

    [Fact]
    public void RemovingADay_TakesItOutOfThePattern()
    {
        var s = Standard();
        s.RemoveDay(DayOfWeek.Thursday);
        Assert.False(s.WorkingDays.Includes(DayOfWeek.Thursday));
    }
}

public class EmployeeProfileTests
{
    private static readonly Guid User = Guid.NewGuid();
    private static readonly Guid Branch = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();

    private static EmployeeProfile New() => new(User, Branch, Dept, new DateOnly(2024, 3, 1));

    [Fact]
    public void SalaryChange_KeepsTheHistory()
    {
        var p = New();
        var change = p.ChangeSalary(8000, new DateOnly(2026, 1, 1), "ترقية", Guid.NewGuid());
        Assert.Equal(8000, p.BasicSalary);
        Assert.Equal(0, change.OldSalary);
        Assert.Equal(8000, change.NewSalary);
    }

    [Fact]
    public void SalaryChange_CannotStartBeforeHireDate() =>
        Assert.Throws<DomainException>(() => New().ChangeSalary(5000, new DateOnly(2023, 1, 1), "x", Guid.NewGuid()));

    [Fact]
    public void Employee_CannotManageThemselves() =>
        Assert.Throws<DomainException>(() => New().SetPlacement(Branch, Dept, null, User));

    [Fact]
    public void EndedService_CannotBeReinstated()
    {
        var p = New();
        p.EndService(new DateOnly(2026, 6, 30), "استقالة");
        Assert.Equal(EmploymentStatus.Ended, p.Status);
        Assert.Throws<DomainException>(p.Reinstate);
    }

    [Fact]
    public void ExpiringDocuments_AreListedSoonestFirst()
    {
        var p = New();
        var today = new DateOnly(2026, 9, 24);
        p.SetDocuments("784", today.AddDays(20), "P1", today.AddDays(5), null, null, "AE07 0331 2345 6789 0123 456");
        var due = p.ExpiringDocuments(today, 30);
        Assert.Equal(2, due.Count);
        Assert.Equal("passport", due[0].Document);
        Assert.Equal("AE070331234567890123456", p.Iban);
    }
}
