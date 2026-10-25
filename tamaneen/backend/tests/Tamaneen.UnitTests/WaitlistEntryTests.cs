using Tamaneen.Clinical.Scheduling;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

public sealed class WaitlistEntryTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; init; } = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    }

    private static readonly IClock Clock = new FixedClock();

    private static readonly TherapistProfile Sara = TherapistProfile.Create(Guid.NewGuid(), "أ", "A", "ب", "B", "female", ["sleep"], ["ar"], licenseVerified: true);

    // 2026-10-30 is a Friday; 07:00 UTC is 10:00 in Riyadh.
    private static readonly DateTimeOffset FridayMorning = new(2026, 10, 30, 7, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SundayEvening = new(2026, 11, 1, 15, 0, 0, TimeSpan.Zero);

    private static WaitlistEntry Entry(Guid? therapist = null, string? gender = null, string? language = null, string[]? times = null, string[]? days = null) =>
        WaitlistEntry.Join(Guid.NewGuid(), therapist, gender, language, times, days, RiskLevel.Routine, "Asia/Riyadh", Clock);

    [Fact]
    public void Empty_preferences_fit_any_time_of_any_therapist()
    {
        Entry().Fits(Sara, FridayMorning).Should().BeTrue();
        Entry().Fits(Sara, SundayEvening).Should().BeTrue();
    }

    [Fact]
    public void Times_and_days_are_read_in_the_patients_time_zone()
    {
        Entry(times: ["morning"], days: ["weekend"]).Fits(Sara, FridayMorning).Should().BeTrue();
        Entry(times: ["morning"]).Fits(Sara, SundayEvening).Should().BeFalse("18:00 in Riyadh is evening");
        Entry(times: ["evening"], days: ["weekdays"]).Fits(Sara, SundayEvening).Should().BeTrue();
        Entry(days: ["weekdays"]).Fits(Sara, FridayMorning).Should().BeFalse("Friday is the weekend");
    }

    [Fact]
    public void Gender_language_and_a_chosen_therapist_must_match()
    {
        Entry(gender: "male").Fits(Sara, FridayMorning).Should().BeFalse();
        Entry(language: "en").Fits(Sara, FridayMorning).Should().BeFalse();
        Entry(gender: "female", language: "ar").Fits(Sara, FridayMorning).Should().BeTrue();
        Entry(therapist: Guid.NewGuid()).Fits(Sara, FridayMorning).Should().BeFalse();
        Entry(therapist: Sara.UserId).Fits(Sara, FridayMorning).Should().BeTrue();
    }

    [Fact]
    public void Unknown_preferences_are_field_errors_and_a_notice_resets_on_new_preferences()
    {
        var act = () => Entry(gender: "robot", times: ["midnight"]);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);

        var entry = Entry();
        var slot = Guid.NewGuid();
        entry.MarkNotified(slot, Clock);
        entry.Status.Should().Be(WaitlistStatus.Notified);
        entry.NotifiedSlotId.Should().Be(slot);
        entry.SetPreferences(null, "female", "any", ["evening"], [], RiskLevel.High, "Asia/Riyadh");
        entry.Status.Should().Be(WaitlistStatus.Waiting);
        entry.NotifiedSlotId.Should().BeNull();
        entry.Leave(Clock);
        entry.Active.Should().BeFalse();
    }
}
