using FieldAttendance.Domain.Common;
using FieldAttendance.Domain.Enums;

namespace FieldAttendance.Domain.Ratings;

public sealed record PresentEmployee(Guid EmployeeId, Guid AttendanceRecordId, string DisplayName);

public sealed record RatingLink(Guid? EmployeeId, Guid? AttendanceRecordId, RatingLinkType LinkType);

/// <summary>Spec 3.10: which employee a QR rating belongs to.</summary>
public static class RatingLinkPolicy
{
    public static RatingLink Decide(IReadOnlyList<PresentEmployee> present, Guid? selectedEmployeeId, bool withinShiftHours)
    {
        ArgumentNullException.ThrowIfNull(present);

        if (present.Count == 0)
            return new RatingLink(null, null, withinShiftHours ? RatingLinkType.NoEmployee : RatingLinkType.OutsideShift);

        PresentEmployee chosen;
        if (present.Count == 1)
        {
            chosen = present[0];
        }
        else
        {
            chosen = present.FirstOrDefault(p => p.EmployeeId == selectedEmployeeId)
                ?? throw new DomainException("rating.select_employee", "Please select the employee who served you.");
        }

        var link = !withinShiftHours ? RatingLinkType.OutsideShift
            : present.Count == 1 ? RatingLinkType.Auto
            : RatingLinkType.CustomerSelected;
        return new RatingLink(chosen.EmployeeId, chosen.AttendanceRecordId, link);
    }
}
