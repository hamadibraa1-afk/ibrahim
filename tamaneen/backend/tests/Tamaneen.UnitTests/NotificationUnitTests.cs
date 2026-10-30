using Tamaneen.Api.Endpoints;
using Tamaneen.Api.Notifications;
using Tamaneen.Clinical.Scheduling;
using Tamaneen.Identity;
using Tamaneen.Identity.Notifications;
using Tamaneen.SharedKernel;
using Tamaneen.SharedKernel.Notifications;

namespace Tamaneen.UnitTests;

public sealed class NotificationUnitTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 25, 9, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void The_null_notifier_does_nothing_and_the_contract_keeps_its_shape()
    {
        var n = new Notification(Guid.NewGuid(), "appointment.missed", new Dictionary<string, string> { ["appointmentId"] = "x" });
        n.Link.Should().BeNull();
        new NullNotifier().NotifyAsync(n, CancellationToken.None).IsCompletedSuccessfully.Should().BeTrue();
    }

    [Theory]
    [InlineData("appointment.cancelled_by_therapist")]
    [InlineData("appointment.running_late")]
    [InlineData("appointment.missed")]
    [InlineData("session.therapist_joined")]
    [InlineData("homework.assigned")]
    [InlineData("homework.feedback")]
    [InlineData("care.ended")]
    [InlineData("care.change_requested")]
    [InlineData("safety_plan.updated")]
    [InlineData("appointment.rebook_needed")]
    [InlineData("appointment.no_show_followup")]
    [InlineData("appointment.rescheduled")]
    [InlineData("appointment.booked")]
    public void Kinds_other_modules_emit_are_in_the_catalog(string kind) => NotificationCatalog.IsKnown(kind).Should().BeTrue();

    [Fact]
    public void Supervisor_follow_ups_are_important_and_open_the_patient()
    {
        var rebook = NotificationCatalog.Find(NotificationKinds.AppointmentRebookNeeded);
        rebook.Important.Should().BeTrue();
        rebook.Category.Should().Be(NotificationCatalog.Appointments);
        var noShow = NotificationCatalog.Find(NotificationKinds.AppointmentNoShowFollowUp);
        noShow.Important.Should().BeTrue();
        noShow.Category.Should().Be(NotificationCatalog.Risk);
        noShow.DefaultLink(new Dictionary<string, string> { ["pseudonymId"] = "P-AAAA-1111" }, staff: true).Should().Be("/patients/P-AAAA-1111");
    }

    [Fact]
    public void Unknown_kinds_are_stored_as_system_notices_and_crisis_ones_as_crisis()
    {
        NotificationCatalog.Find("something.new").Category.Should().Be(NotificationCatalog.System);
        var crisis = NotificationCatalog.Find("crisis.something");
        crisis.Category.Should().Be(NotificationCatalog.Risk);
        crisis.Crisis.Should().BeTrue();
    }

    [Fact]
    public void Default_links_fill_ids_and_fall_back_to_the_list()
    {
        var kind = NotificationCatalog.Find(NotificationKinds.AppointmentMissed);
        kind.DefaultLink(new Dictionary<string, string> { ["appointmentId"] = "abc" }, staff: false).Should().Be("/appointments/abc");
        kind.DefaultLink(new Dictionary<string, string>(), staff: false).Should().Be("/appointments");
        NotificationCatalog.Find(NotificationKinds.StaffAppointmentBooked).DefaultLink(new Dictionary<string, string> { ["pseudonymId"] = "P-AAAA-1111" }, staff: true).Should().Be("/patients/P-AAAA-1111");
        NotificationCatalog.Find(NotificationKinds.CrisisFollowUp).DefaultLink(new Dictionary<string, string>(), staff: false).Should().Be("/crisis/after");
    }

    [Fact]
    public void Templates_need_every_value_they_name()
    {
        var values = new Dictionary<string, string> { ["therapist"] = "Sara" };
        NotificationTexts.Fill("Hi {therapist}", values).Should().Be("Hi Sara");
        NotificationTexts.Fill("At {time} with {therapist}", values).Should().BeNull();
        NotificationTexts.Fill(null, values).Should().BeNull();
    }

    [Fact]
    public void Times_are_written_in_the_readers_zone_with_latin_digits()
    {
        var at = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
        NotificationTexts.FormatTime(at, "en", "Asia/Riyadh").Should().Be("Monday 26 October 4:00 PM");
        NotificationTexts.FormatTime(at, "ar", "Asia/Riyadh").Should().MatchRegex("^[^0-9]+ 26 [^0-9]+ 4:00 ");
    }

    [Fact]
    public void Only_short_plain_values_are_kept()
    {
        var clean = Notifier.Sanitize(new Dictionary<string, string>
        {
            ["appointmentId"] = "a1",
            ["note"] = new string('x', 500),
            ["bad key"] = "v",
            ["multi"] = "line\nbreak",
        });
        clean.Should().ContainKey("appointmentId").And.HaveCount(1);
    }

    [Fact]
    public void Copies_follow_the_channel_switch_the_sms_fallback_and_always_go_for_crisis()
    {
        var prefs = NotificationPreferences.Default(Guid.NewGuid());
        prefs.Update(push: true, sms: false, email: false, neutralText: false, quickExit: false, ReminderFrequency.Daily, "21:00", "23:00", "08:00");
        Notifier.WantsCopy(NotificationCatalog.Find(NotificationKinds.HomeworkAssigned), prefs, "sms").Should().BeFalse();
        Notifier.WantsCopy(NotificationCatalog.Find(NotificationKinds.AppointmentReminder1h), prefs, "sms").Should().BeTrue("important kinds fall back to SMS while app notifications are on");

        prefs.Update(push: false, sms: false, email: true, neutralText: false, quickExit: false, ReminderFrequency.Daily, "21:00", "23:00", "08:00");
        Notifier.WantsCopy(NotificationCatalog.Find(NotificationKinds.AppointmentReminder1h), prefs, "sms").Should().BeFalse();
        Notifier.WantsCopy(NotificationCatalog.Find(NotificationKinds.HomeworkAssigned), prefs, "email").Should().BeTrue();
        Notifier.WantsCopy(NotificationCatalog.Find(NotificationKinds.CrisisFollowUp), prefs, "sms").Should().BeTrue();
    }

    [Fact]
    public void Quiet_hours_end_on_the_same_or_the_next_day()
    {
        var prefs = NotificationPreferences.Default(Guid.NewGuid());
        prefs.IsQuiet(new TimeOnly(23, 30)).Should().BeTrue();
        prefs.IsQuiet(new TimeOnly(7, 59)).Should().BeTrue();
        prefs.IsQuiet(new TimeOnly(8, 0)).Should().BeFalse();
        prefs.QuietEndsAfter(new DateTime(2026, 10, 25, 23, 30, 0)).Should().Be(new DateTime(2026, 10, 26, 8, 0, 0));
        prefs.QuietEndsAfter(new DateTime(2026, 10, 26, 2, 0, 0)).Should().Be(new DateTime(2026, 10, 26, 8, 0, 0));
    }

    [Fact]
    public void A_failed_copy_is_retried_with_a_growing_wait_then_given_up()
    {
        var clock = new FixedClock();
        var n = UserNotification.Create(Guid.NewGuid(), "x.y", "system", "{}", "https://evil.example", null, clock);
        n.Link.Should().BeNull("only app-relative links are kept");
        n.QueueExternal("sms", clock.UtcNow);
        for (var i = 1; i < UserNotification.MaxAttempts; i++)
        {
            n.ExternalFailed(clock);
            n.External.Should().Be(ExternalDelivery.Pending);
            n.ExternalNotBeforeUtc.Should().Be(clock.UtcNow.AddMinutes(i));
        }

        n.ExternalFailed(clock);
        n.External.Should().Be(ExternalDelivery.Failed);
        n.MarkRead(clock);
        n.IsRead.Should().BeTrue();
    }

    [Fact]
    public void Reminder_keys_change_when_the_appointment_moves()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.Zero);
        NotificationWorker.ReminderKey(NotificationKinds.AppointmentReminder24h, id, at).Should().Be(NotificationWorker.ReminderKey(NotificationKinds.AppointmentReminder24h, id, at));
        NotificationWorker.ReminderKey(NotificationKinds.AppointmentReminder24h, id, at.AddHours(1)).Should().NotBe(NotificationWorker.ReminderKey(NotificationKinds.AppointmentReminder24h, id, at));
    }

    [Theory]
    [InlineData("اتصل بي على 0551234567", true)]
    [InlineData("رقمي ٠٥٥١٢٣٤٥٦٧", true)]
    [InlineData("write to me@example.com", true)]
    [InlineData("see https://x.io", true)]
    [InlineData("visit www.clinic.sa", true)]
    [InlineData("أعمل منذ 12 سنة مع البالغين", false)]
    [InlineData("I use CBT and mindfulness.", false)]
    public void Profile_text_may_not_carry_contact_details(string text, bool contact) => StaffProfileEndpoints.HasContactDetails(text).Should().Be(contact);

    [Fact]
    public void Capacity_cannot_go_below_the_caseload()
    {
        var p = TherapistProfile.Create(Guid.NewGuid(), "أ", "A", "ب", "B", "female", ["sleep"], ["ar"], licenseVerified: true);
        var act = () => p.SetCapacity(3, 4);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.CapacityBelowCaseload);
        p.SetCapacity(4, 4);
        p.Capacity.Should().Be(4);
    }

    [Fact]
    public void The_notification_service_is_a_service_identity()
    {
        AppRoles.IsService(AppRoles.NotificationService).Should().BeTrue();
        AppRoles.IsKnown(AppRoles.NotificationService).Should().BeTrue();
    }
}
