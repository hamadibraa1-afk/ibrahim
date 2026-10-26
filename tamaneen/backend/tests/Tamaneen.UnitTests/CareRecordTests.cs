using Tamaneen.Clinical.Care;
using Tamaneen.Clinical.Intake;
using Tamaneen.SharedKernel;

namespace Tamaneen.UnitTests;

/// <summary>Phase 5 care records: session notes, treatment plans, homework edits, safety-plan versions, closures and change requests.</summary>
public class CareRecordTests
{
    private readonly TestClock _clock = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));

    private static readonly byte[] Cipher = [1, 2, 3];

    [Fact]
    public void A_note_is_a_draft_until_signed_then_locked_and_only_takes_addenda()
    {
        var note = SessionNote.Start(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), _clock);
        var act = () => SessionNoteAddendum.Add(note, Guid.NewGuid(), Cipher, _clock);
        act.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.NoteNotSigned);

        note.SaveDraft("soap", Cipher, null, shareSummary: true, _clock);
        note.Revision.Should().Be(1);
        note.ShareSummary.Should().BeFalse("nothing to share without points");
        var unknown = () => note.SaveDraft("birp", Cipher, null, false, _clock);
        unknown.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);

        note.Sign(Guid.NewGuid(), _clock);
        note.Status.Should().Be(SessionNoteStatus.Locked);
        note.Revision.Should().Be(2);
        var edit = () => note.SaveDraft("soap", Cipher, null, false, _clock);
        edit.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.NoteSigned);
        var sign = () => note.Sign(Guid.NewGuid(), _clock);
        sign.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.NoteSigned);
        SessionNoteAddendum.Add(note, Guid.NewGuid(), Cipher, _clock).NoteId.Should().Be(note.Id);
    }

    [Fact]
    public void Plan_versions_count_up_and_need_a_future_review_date()
    {
        var today = new DateOnly(2026, 10, 20);
        var v = TreatmentPlanVersion.Create(Guid.NewGuid(), Guid.NewGuid(), 2, Cipher, SessionFrequency.Weekly, today.AddDays(14), today, _clock);
        v.Version.Should().Be(3);
        var past = () => TreatmentPlanVersion.Create(Guid.NewGuid(), Guid.NewGuid(), 0, Cipher, SessionFrequency.Weekly, today, today, _clock);
        past.Should().Throw<DomainException>();
    }

    [Fact]
    public void Cancelled_homework_counts_as_finished_and_cannot_be_edited()
    {
        var today = new DateOnly(2026, 10, 20);
        var h = HomeworkAssignment.Assign(Guid.NewGuid(), Guid.NewGuid(), Cipher, Cipher, 5, HomeworkFrequency.Daily, today, today.AddDays(6), _clock);
        h.Update(Cipher, Cipher, 10, HomeworkFrequency.Weekly, today.AddDays(10), today, _clock);
        h.EndsOn.Should().Be(today.AddDays(10));
        h.IsCompleted(today).Should().BeFalse();
        h.Cancel(today, _clock);
        h.IsCompleted(today).Should().BeTrue();
        var edit = () => h.Update(Cipher, Cipher, 10, HomeworkFrequency.Weekly, today.AddDays(10), today, _clock);
        edit.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.HomeworkEnded);
    }

    [Fact]
    public void Every_safety_plan_save_adds_a_version()
    {
        var plan = SafetyPlan.Create(Guid.NewGuid());
        plan.Version.Should().Be(0);
        plan.Update(Cipher, Guid.NewGuid(), AppRoles.Patient, _clock);
        var therapist = Guid.NewGuid();
        plan.Update(Cipher, therapist, AppRoles.Therapist, _clock);
        plan.Version.Should().Be(2);
        plan.CoAuthorTherapistId.Should().Be(therapist);
        var version = SafetyPlanVersion.Of(plan);
        version.Version.Should().Be(2);
        version.AuthorRole.Should().Be(AppRoles.Therapist);
    }

    [Fact]
    public void A_closure_goes_draft_submitted_signed_off_and_the_author_cannot_sign_it()
    {
        var therapist = Guid.NewGuid();
        var closure = CareClosure.Start(Guid.NewGuid(), therapist, _clock);
        var empty = () => closure.Submit(_clock);
        empty.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.Validation);
        closure.Save("goals_met", 8, 10, Cipher, Cipher, "phq9_1m_3m", true, _clock);
        closure.Submit(_clock);
        var edit = () => closure.Save("goals_met", 8, 10, Cipher, Cipher, "none", true, _clock);
        edit.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ClosureLocked);
        var own = () => closure.SignOff(therapist, _clock);
        own.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ClosureSignOffNotAllowed);
        closure.Reject(Guid.NewGuid(), Cipher, _clock);
        closure.Status.Should().Be(CareClosureStatus.Rejected);
        closure.Save("goals_met", 8, 10, Cipher, Cipher, "none", true, _clock);
        closure.Submit(_clock);
        closure.SignOff(Guid.NewGuid(), _clock);
        closure.Status.Should().Be(CareClosureStatus.SignedOff);
        var again = () => closure.SignOff(Guid.NewGuid(), _clock);
        again.Should().Throw<DomainException>().Which.Code.Should().Be(ErrorCodes.ClosureNotSubmitted);
        var bad = () => CareClosure.Start(Guid.NewGuid(), therapist, _clock).Save("bored", 1, 1, null, null, "none", true, _clock);
        bad.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_change_request_takes_only_catalog_reasons_and_its_case_is_due_within_the_promise()
    {
        var act = () => ChangeTherapistRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "free text", Guid.NewGuid(), _clock);
        act.Should().Throw<DomainException>();
        var request = ChangeTherapistRequest.Create(Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid(), _clock);
        request.Accept(Guid.NewGuid(), _clock);
        request.Status.Should().Be(ChangeTherapistStatus.Accepted);
        request.Close(_clock);
        request.Status.Should().Be(ChangeTherapistStatus.Accepted, "a finished request stays as it ended");

        var routine = TriageCase.ForChange(null, Guid.NewGuid(), TimeSpan.FromHours(24), _clock);
        routine.Path.Should().Be(TriageCase.ChangePath);
        routine.IsChangeRequest.Should().BeTrue();
        routine.DueAtUtc.Should().Be(_clock.UtcNow.AddHours(24));
        var clinical = TriageCase.Direct(Guid.NewGuid(), RiskLevel.High, true, _clock);
        var urgent = TriageCase.ForChange(clinical, clinical.PatientId, TimeSpan.FromHours(24), _clock);
        urgent.RiskLevel.Should().Be(RiskLevel.High);
        urgent.DueAtUtc.Should().Be(_clock.UtcNow.AddHours(1), "a high-risk patient's case keeps the shorter review deadline");
    }
}
