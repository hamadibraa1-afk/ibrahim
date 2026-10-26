namespace Tamaneen.Api.Notifications;

/// <summary>Every kind the platform emits. The texts live in the API resources as notify.&lt;kind&gt; (in app) and notify.sms.&lt;kind&gt; (SMS and e-mail).</summary>
public static class NotificationKinds
{
    // Patient: appointments
    public const string AppointmentBooked = "appointment.booked";
    public const string AppointmentRescheduled = "appointment.rescheduled";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string AppointmentCancelledByTherapist = "appointment.cancelled_by_therapist";
    public const string AppointmentRunningLate = "appointment.running_late";
    public const string AppointmentMissed = "appointment.missed";
    public const string AppointmentReminder24h = "appointment.reminder_24h";
    public const string AppointmentReminder1h = "appointment.reminder_1h";
    public const string SessionTherapistJoined = "session.therapist_joined";
    public const string WaitlistSlotOpen = "waitlist.slot_open";

    // Patient: care
    public const string TriageMatchesReady = "triage.matches_ready";
    public const string HomeworkAssigned = "homework.assigned";
    public const string HomeworkFeedback = "homework.feedback";
    public const string CareEnded = "care.ended";
    public const string SafetyPlanUpdated = "safety_plan.updated";
    public const string CrisisFollowUp = "crisis.follow_up";

    // Anyone
    public const string SecurityNewSignIn = "security.new_sign_in";

    // Staff
    public const string CareChangeRequested = "care.change_requested";
    public const string StaffAppointmentBooked = "staff.appointment.booked";
    public const string StaffAppointmentRescheduled = "staff.appointment.rescheduled";
    public const string StaffAppointmentCancelled = "staff.appointment.cancelled";
    public const string CrisisClosed = "crisis.closed";
}

/// <summary>
/// How one kind behaves. <see cref="Crisis"/> kinds ignore quiet hours and every channel switch (P33: "تنبيهات
/// المستوى 3–4 تتجاهل ساعات الهدوء ولا يمكن إيقافها"). <see cref="Important"/> kinds fall back to SMS or e-mail
/// while app notifications are on, because the web app has no push channel yet (P33: "التذكيرات المهمة لها بديل
/// SMS"). <see cref="StaffBookings"/> kinds follow the staff member's S32 "new booking or cancellation" switch.
/// </summary>
public sealed record NotificationKind(string Code, string Category, bool Crisis = false, bool Important = false, bool StaffBookings = false, string? PatientLink = null, string? StaffLink = null)
{
    /// <summary>The deep link for a recipient when the emitter gave none. {key} is replaced from the data.</summary>
    public string? DefaultLink(IReadOnlyDictionary<string, string> data, bool staff)
    {
        var template = staff ? StaffLink : PatientLink;
        if (template is null)
        {
            return null;
        }

        foreach (var (key, value) in data)
        {
            template = template.Replace("{" + key + "}", Uri.EscapeDataString(value), StringComparison.Ordinal);
        }

        // A placeholder left over means the data lacked an id: fall back to the list page before it.
        var brace = template.IndexOf('{', StringComparison.Ordinal);
        return brace < 0 ? template : template[..Math.Max(1, brace - 1)];
    }
}

public static class NotificationCatalog
{
    public const string Risk = "risk";
    public const string Appointments = "appointments";
    public const string Messages = "messages";
    public const string Homework = "homework";
    public const string Care = "care";
    public const string Security = "security";
    public const string System = "system";

    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.Ordinal) { Risk, Appointments, Messages, Homework, Care, Security, System };

    private static readonly NotificationKind[] All =
    [
        new(NotificationKinds.AppointmentBooked, Appointments, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentRescheduled, Appointments, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentCancelled, Appointments, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentCancelledByTherapist, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentRunningLate, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentMissed, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentReminder24h, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.AppointmentReminder1h, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.SessionTherapistJoined, Appointments, Important: true, PatientLink: "/appointments/{appointmentId}"),
        new(NotificationKinds.WaitlistSlotOpen, Appointments, Important: true, PatientLink: "/waitlist"),
        new(NotificationKinds.TriageMatchesReady, Care, Important: true, PatientLink: "/matches"),
        new(NotificationKinds.HomeworkAssigned, Homework, PatientLink: "/homework/{homeworkId}"),
        new(NotificationKinds.HomeworkFeedback, Homework, PatientLink: "/homework/{homeworkId}"),
        new(NotificationKinds.CareEnded, Care, PatientLink: "/home"),
        new(NotificationKinds.SafetyPlanUpdated, Care, PatientLink: "/safety-plan", StaffLink: "/patients/{pseudonymId}"),
        new(NotificationKinds.CrisisFollowUp, Risk, Crisis: true, PatientLink: "/crisis/after"),
        new(NotificationKinds.SecurityNewSignIn, Security, PatientLink: "/settings/security", StaffLink: "/settings"),
        new(NotificationKinds.CareChangeRequested, Care, StaffLink: "/triage"),
        new(NotificationKinds.StaffAppointmentBooked, Appointments, StaffBookings: true, StaffLink: "/patients/{pseudonymId}"),
        new(NotificationKinds.StaffAppointmentRescheduled, Appointments, StaffBookings: true, StaffLink: "/patients/{pseudonymId}"),
        new(NotificationKinds.StaffAppointmentCancelled, Appointments, StaffBookings: true, StaffLink: "/patients/{pseudonymId}"),
        new(NotificationKinds.CrisisClosed, Risk, StaffLink: "/alerts"),
    ];

    private static readonly Dictionary<string, NotificationKind> ByCode = All.ToDictionary(k => k.Code, StringComparer.Ordinal);

    public static IReadOnlyCollection<NotificationKind> Kinds => All;

    /// <summary>A kind nobody catalogued yet is stored as a plain system notice with the generic text.</summary>
    public static NotificationKind Find(string code) =>
        ByCode.TryGetValue(code, out var kind) ? kind : new NotificationKind(code, code.StartsWith("crisis.", StringComparison.Ordinal) ? Risk : System, Crisis: code.StartsWith("crisis.", StringComparison.Ordinal));

    public static bool IsKnown(string code) => ByCode.ContainsKey(code);
}
