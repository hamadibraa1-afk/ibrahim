using Tamaneen.Clinical.Scheduling;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

/// <summary>S19: generating bookable times from the usual week, and reconciling them with what is booked (SB-28).</summary>
public sealed class AvailabilityPlannerTests
{
    private static readonly TimeZoneInfo Riyadh = TimeZoneInfo.CreateCustomTimeZone("Arabia", TimeSpan.FromHours(3), "Arabia", "Arabia");

    // Sunday 1 November 2026, 06:00 in Riyadh.
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 3, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Notice = TimeSpan.FromHours(2);

    private static DateTimeOffset Local(int day, int hour, int minute = 0) => new DateTimeOffset(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(3)).ToUniversalTime();

    [Fact]
    public void Periods_fill_with_sessions_and_gaps_for_eight_weeks()
    {
        var times = AvailabilityPlanner.Generate([new WeeklyPeriod(0, "10:00", "13:00")], [], 50, 10, Riyadh, Now, Notice);
        times.Should().HaveCount(3 * 8, "10:00, 11:00 and 12:00 fit; 13:00 would end after the period");
        times.Take(3).Select(t => t.StartsAtUtc).Should().Equal(Local(1, 10), Local(1, 11), Local(1, 12));
        times.Should().OnlyContain(t => TimeZoneInfo.ConvertTime(t.StartsAtUtc, Riyadh).DayOfWeek == DayOfWeek.Sunday);
    }

    [Fact]
    public void Nothing_is_generated_inside_the_minimum_notice_or_a_leave()
    {
        var early = AvailabilityPlanner.Generate([new WeeklyPeriod(0, "07:00", "09:00")], [], 50, 0, Riyadh, Now, Notice);
        early.Should().NotContain(t => t.StartsAtUtc <= Now + Notice);
        early[0].StartsAtUtc.Should().Be(Local(8, 7), "today's 07:00 and 07:50 are within two hours");

        var leave = new AvailabilityBreak(Guid.NewGuid(), "leave", Local(8, 0), Local(15, 0));
        var times = AvailabilityPlanner.Generate([new WeeklyPeriod(0, "10:00", "11:00")], [leave], 50, 0, Riyadh, Now, Notice);
        times.Select(t => t.StartsAtUtc).Should().NotContain(Local(8, 10)).And.Contain(Local(1, 10)).And.Contain(Local(15, 10));
    }

    [Fact]
    public void Overlapping_or_reversed_periods_are_field_errors()
    {
        var errors = TherapistAvailability.Validate(
            [new WeeklyPeriod(1, "10:00", "12:00"), new WeeklyPeriod(1, "11:30", "13:00"), new WeeklyPeriod(2, "15:00", "14:00"), new WeeklyPeriod(9, "10:00", "11:00")],
            [new AvailabilityBreak(Guid.NewGuid(), "holiday", Now, Now.AddDays(1))],
            7);
        errors.Should().ContainKey("weekly.1").WhoseValue.Should().Equal("overlap");
        errors.Should().ContainKey("weekly.2").WhoseValue.Should().Equal("end_before_start");
        errors.Should().ContainKey("weekly.3");
        errors.Should().ContainKey("exceptions.0");
        errors.Should().ContainKey("bufferMinutes");
        TherapistAvailability.Validate([new WeeklyPeriod(1, "10:00", "12:00"), new WeeklyPeriod(1, "12:00", "13:00")], [], 10).Should().BeEmpty("touching periods do not overlap");
    }

    [Fact]
    public void Booked_and_held_times_are_kept_and_reported_open_ones_follow_the_pattern()
    {
        var clock = new TestClock(Now);
        var therapist = Guid.NewGuid();
        var booked = AvailabilitySlot.Create(therapist, Local(2, 10), 50);
        booked.Hold(Guid.NewGuid(), clock);
        booked.TakeFor(booked.HeldByPatientId!.Value, clock);
        var held = AvailabilitySlot.Create(therapist, Local(2, 11), 50);
        held.Hold(Guid.NewGuid(), clock);
        var open = AvailabilitySlot.Create(therapist, Local(2, 12), 50);
        var oneOff = AvailabilitySlot.Create(therapist, Local(2, 16), 50, SlotSource.Manual);

        var desired = AvailabilityPlanner.Generate([new WeeklyPeriod(1, "10:00", "11:00")], [], 50, 0, Riyadh, Now, Notice);
        var plan = AvailabilityPlanner.Reconcile([booked, held, open, oneOff], desired, [], clock);

        plan.ToRemove.Should().BeEquivalentTo([open]);
        plan.Affected.Should().BeEquivalentTo([held], "the booked 10:00 is still in the pattern; the held 11:00 is not");
        plan.ToAdd.Should().NotContain(t => t.StartsAtUtc == Local(2, 10, 0), "the booked time already covers it");
        plan.ToAdd.Should().HaveCount(7, "the other seven Mondays");
    }

    [Fact]
    public void A_new_time_never_overlaps_a_kept_one()
    {
        var clock = new TestClock(Now);
        var oneOff = AvailabilitySlot.Create(Guid.NewGuid(), Local(1, 10, 30), 50, SlotSource.Manual);
        var desired = AvailabilityPlanner.Generate([new WeeklyPeriod(0, "10:00", "12:00")], [], 50, 0, Riyadh, Now, Notice);
        var plan = AvailabilityPlanner.Reconcile([oneOff], desired, [], clock);
        plan.ToAdd.Where(t => t.StartsAtUtc.Date == Local(1, 10).Date).Should().BeEmpty("10:00 and 10:50 both overlap the one-off 10:30");
    }

    [Fact]
    public void A_taken_slot_cannot_be_blocked_or_moved_but_a_free_one_can()
    {
        var clock = new TestClock(Now);
        var slot = AvailabilitySlot.Create(Guid.NewGuid(), Local(3, 10), 50);
        slot.Hold(Guid.NewGuid(), clock);
        FluentActions.Invoking(() => slot.Block(clock)).Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.SlotTaken);
        FluentActions.Invoking(() => slot.MoveTo(Local(3, 12), 50, clock)).Should().Throw<DomainException>();

        clock.Advance(AvailabilitySlot.HoldDuration);
        slot.MoveTo(Local(3, 12), 50, clock);
        slot.Source.Should().Be(SlotSource.Manual);
        slot.Block(clock);
        slot.IsAvailable(clock).Should().BeFalse("a blocked time is never bookable");
        slot.Reopen(45);
        slot.IsAvailable(clock).Should().BeTrue();
        slot.DurationMinutes.Should().Be(45);
    }

    [Fact]
    public void Running_late_and_therapist_cancel_follow_the_session_window()
    {
        var clock = new TestClock(Now);
        var slot = AvailabilitySlot.Create(Guid.NewGuid(), Now.AddHours(3), 50);
        var appointment = Appointment.Book(Guid.NewGuid(), slot, 1, "room", clock);
        FluentActions.Invoking(() => appointment.ReportLate(10, clock)).Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.AppointmentNotLateable);

        clock.Advance(TimeSpan.FromMinutes(175));
        FluentActions.Invoking(() => appointment.ReportLate(61, clock)).Should().Throw<ArgumentOutOfRangeException>();
        appointment.ReportLate(10, clock);
        appointment.TherapistLateMinutes.Should().Be(10);

        FluentActions.Invoking(() => appointment.CancelByTherapist("bored", null, clock)).Should().Throw<ArgumentOutOfRangeException>();
        appointment.CancelByTherapist("emergency", null, clock);
        appointment.CancelledBy.Should().Be("therapist");
        appointment.CancelReason.Should().Be("emergency");
        appointment.Missed.Should().BeFalse();
        FluentActions.Invoking(() => appointment.CancelByTherapist("emergency", null, clock)).Should().Throw<DomainException>();
    }
}
