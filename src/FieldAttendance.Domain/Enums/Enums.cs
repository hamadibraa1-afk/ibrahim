namespace FieldAttendance.Domain.Enums;

/// <summary>SystemAdmin manages users and settings; DepartmentManager is read-only oversight.</summary>
public enum UserRole
{
    Supervisor = 1,
    Collector = 2,
    SystemAdmin = 3,
    DepartmentManager = 4,
    /// <summary>Owns HR data end to end: profiles, leave policy, discipline, payroll up to approval.</summary>
    HrManager = 5,
    /// <summary>Day-to-day HR work without final approvals.</summary>
    HrOfficer = 6,
    /// <summary>Office employee: sees only their own record.</summary>
    Employee = 7,
}

public enum OverrideType { TemporaryTransfer = 1, Replacement = 2, EmergencyCover = 3, Cancel = 4 }

public enum CheckInType { Normal = 1, Exception = 2 }

public enum CheckOutType { Normal = 1, Exception = 2, Auto = 3, UnreturnedExit = 4 }

/// <summary>Persisted day status. "On temporary exit" is derived from an open exit, not stored.</summary>
public enum AttendanceStatus { Scheduled = 1, Present = 2, CheckedOut = 3, Absent = 4, OnLeave = 5 }

public enum PermissionType { Late = 1, TemporaryExit = 2, EarlyDeparture = 3 }

public enum RequestStatus { Pending = 1, Approved = 2, Rejected = 3, Cancelled = 4 }

public enum ExceptionKind { CheckIn = 1, CheckOut = 2 }

/// <summary>Field sites hold collectors; office sites are branches where administrative staff work.</summary>
public enum LocationKind { Field = 1, Office = 2 }

/// <summary>Where an employee stands in their working life, not their attendance for a day.</summary>
public enum EmploymentStatus { Active = 1, OnLeave = 2, Suspended = 3, Ended = 4 }

/// <summary>How a deduction is measured before it is turned into money.</summary>
/// <summary>The kinds of request that travel through an approval chain.</summary>
/// <summary>What a notification is about. The text lives in the client's dictionary, not here.</summary>
public enum NotificationKind
{
    RequestAwaitingYou = 1,
    RequestDecided = 2,
    ReturnOverdue = 3,
    DocumentExpiring = 4,
    LocationUncovered = 5,
    DeductionProposed = 6,
    PayrollBlocked = 7,
    WarningIssued = 8,
}

public enum NotificationChannel { InApp = 1, Email = 2, Sms = 3 }

public enum RequestKind { Leave = 1, Permission = 2, AttendanceException = 3, ReturnToWork = 4, OfficialDuty = 5 }

/// <summary>Who signs at a level. The chain itself is configured, not hard-coded.</summary>
public enum ApprovalStage { SectionHead = 1, LineManager = 2, DepartmentManager = 3, Hr = 4, Executive = 5 }

public enum ApprovalStepStatus { Pending = 1, Approved = 2, Rejected = 3, Skipped = 4 }

public enum ReturnStatus { Expected = 1, Submitted = 2, Confirmed = 3, Late = 4 }

public enum DeductionUnit { Day = 1, Hour = 2, FixedAmount = 3 }

/// <summary>What the system watches for when proposing a deduction.</summary>
public enum DeductionTrigger { Manual = 1, Late = 2, Absence = 3, EarlyDeparture = 4, MissingCheckOut = 5 }

/// <summary>A deduction is only ever proposed; a person decides what happens to it.</summary>
public enum DeductionStatus { Proposed = 1, Approved = 2, ConvertedToWarning = 3, Cancelled = 4 }

public enum PayrollStatus { Draft = 1, Review = 2, Approved = 3, Closed = 4 }

public enum FeedbackKind { Complaint = 1, Suggestion = 2 }

public enum FeedbackStatus { New = 1, InProgress = 2, Escalated = 3, Closed = 4 }

public enum RatingLinkType { Auto = 1, CustomerSelected = 2, NoEmployee = 3, OutsideShift = 4 }

[Flags]
public enum WorkDays
{
    None = 0,
    Sunday = 1 << 0,
    Monday = 1 << 1,
    Tuesday = 1 << 2,
    Wednesday = 1 << 3,
    Thursday = 1 << 4,
    Friday = 1 << 5,
    Saturday = 1 << 6,
    All = Sunday | Monday | Tuesday | Wednesday | Thursday | Friday | Saturday,
}

public static class WorkDaysExtensions
{
    public static bool Includes(this WorkDays days, DayOfWeek day) =>
        (days & (WorkDays)(1 << (int)day)) != 0;
}
