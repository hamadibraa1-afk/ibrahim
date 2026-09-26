using FieldAttendance.Domain.Time;

namespace FieldAttendance.Domain.Tests;

/// <summary>Sunday 20 Sep 2026 is day 0. Standard test shift: 08:00–16:00, grace 10, no break.</summary>
internal static class TestTime
{
    public static readonly DateOnly Day0 = new(2026, 9, 20);

    public static DateTimeOffset T(int hour, int minute, int second = 0, int dayOffset = 0) =>
        UaeTime.At(Day0.AddDays(dayOffset), new TimeOnly(hour, minute, second));

    public static TimeInterval Span(int h1, int m1, int h2, int m2) => new(T(h1, m1), T(h2, m2));

    public static TimeInterval StandardShift => Span(8, 0, 16, 0);
}
