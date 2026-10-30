import { HttpHeaders } from '@angular/common/http';
import type { ClosureView, PlanHistory, SessionNoteView, StaffHomeworkList, StaffSafetyPlan } from '../../core/api/types';
import { API, problem } from '../../testing/fixtures';
import { PID, PATIENT_URL, overview, renderCare, type } from './care-testing';
import { EndOfCareComponent, HAS_NUMBERS } from './end-of-care.component';
import { PatientHomeworkComponent, localDate } from './patient-homework.component';
import { PatientOverviewComponent } from './patient-overview.component';
import { SafetyPlanBuilderComponent } from './safety-plan-builder.component';
import { SessionNotesComponent } from './session-notes.component';
import { TreatmentPlanComponent, planDraftKey } from './treatment-plan.component';
import { TestBed } from '@angular/core/testing';
import { SessionService } from '../../core/session/session.service';

afterEach(() => {
  document.querySelector('.cdk-overlay-container')?.replaceChildren();
});

describe('PatientOverviewComponent (S14)', () => {
  it('shows risk, weeks in care, the mood trend with a table, plan goals, homework and the tabs', async () => {
    const { backend, update, q, el } = await renderCare(PatientOverviewComponent, { id: PID });
    backend.expectOne(`${PATIENT_URL}/overview`).flush(overview());
    await update();
    expect(q('overview-pseudonym')!.textContent).toContain(PID);
    expect(q('risk-tag')!.textContent).toContain('1 · routine');
    expect(q('weeks')!.textContent).toContain('In care for 5 weeks');
    expect(q('mood-average')!.textContent).toContain('6.4');
    expect(q('mood-table')!.querySelectorAll('tr').length).toBe(3);
    expect(q('plan-goals')!.textContent).toContain('version 2');
    expect(q('plan-goals')!.textContent).toContain('Improving');
    expect(q('homework-week')!.textContent).toContain('6 / 7');
    expect(q('tab-file')!.getAttribute('href')).toBe(`/patients/${PID}/file`);
    expect(q('tab-overview')!.getAttribute('aria-current')).toBe('page');
    expect(q('end-care')!.getAttribute('href')).toBe(`/patients/${PID}/close`);
    expect(q('past-note')!.textContent).toContain('Open the signed note');
    expect(el.querySelector('[data-testid="next-note"]')).toBeNull();
    backend.verify();
  });

  it('shows the empty states in Arabic, read-only after the end of care', async () => {
    const { backend, update, q } = await renderCare(PatientOverviewComponent, { id: PID }, { language: 'ar', role: 'supervisor' });
    backend.expectOne(`${PATIENT_URL}/overview`).flush(overview({ mood: [], plan: null, nextAppointment: null, inCare: false, canEdit: false, homework: [] }));
    await update();
    expect(q('no-mood')!.textContent).toContain('لا قياسات بعد');
    expect(q('no-plan')!.textContent).toContain('لا خطة بعد');
    expect(q('no-session')).not.toBeNull();
    expect(q('read-only')!.textContent).toContain('للقراءة فقط');
    expect(q('raise-risk')).toBeNull();
    expect(document.documentElement.dir).toBe('rtl');
  });

  it('says «not found» for a patient who is not assigned (EC-42)', async () => {
    const { backend, update, q } = await renderCare(PatientOverviewComponent, { id: PID });
    const p = problem(404, 'Not found');
    backend.expectOne(`${PATIENT_URL}/overview`).flush(p.body, p.options);
    await update();
    expect(q('not-found')).not.toBeNull();
  });
});

function homeworkList(): StaffHomeworkList {
  return {
    pseudonymId: PID,
    canEdit: true,
    past: [],
    current: [
      {
        id: 'hw-1',
        title: 'Thought record',
        instructions: ['Note the situation.'],
        minutes: 10,
        frequency: 'twice_weekly',
        startsOn: '2026-10-14',
        endsOn: '2030-10-28',
        status: 'active',
        templateCode: 'thought_record',
        doneThisWeek: 2,
        weeklyTarget: 2,
        averageFeeling: 6,
        latestCheckin: { id: 'ci-1', day: '2026-10-19', status: 'done', feeling: 6, note: 'I thought my manager was angry', gateLevel: 3, feedback: null, feedbackAt: null, feedbackSeen: false },
        checkins: [],
      },
    ],
  };
}

describe('PatientHomeworkComponent (S16)', () => {
  async function render() {
    const r = await renderCare(PatientHomeworkComponent, { id: PID });
    r.backend.expectOne(`${PATIENT_URL}/homework`).flush(homeworkList());
    r.backend.expectOne(`${API}/v1/staff/homework/templates`).flush([{ code: 'breathing', title: 'Breathing before bed', instructions: ['Sit somewhere quiet.', 'Breathe in for 4 seconds.'], frequency: 'daily', minutes: 5 }]);
    await r.update();
    return r;
  }

  it('lists the homework with progress, the latest note and a risk badge for a gated note', async () => {
    const { q } = await render();
    expect(q('homework-row')!.textContent).toContain('Twice a week');
    expect(q('homework-row')!.textContent).toContain('2 / 2');
    expect(q('latest-note')!.textContent).toContain('I thought my manager was angry');
    expect(q('risk-badge')).not.toBeNull();
  });

  it('fills the form from the library, previews it and assigns it', async () => {
    const { q, update, backend } = await render();
    q('template-breathing')!.click();
    await update();
    expect((q('hw-title') as HTMLInputElement).value).toBe('Breathing before bed');
    q('hw-preview-button')!.click();
    await update();
    expect(q('hw-preview')!.querySelectorAll('li').length).toBe(2);
    q('hw-save')!.click();
    const post = backend.expectOne(`${PATIENT_URL}/homework`);
    expect(post.request.body).toEqual(jasmine.objectContaining({ title: 'Breathing before bed', instructions: ['Sit somewhere quiet.', 'Breathe in for 4 seconds.'], frequency: 'daily', templateCode: 'breathing', startsOn: localDate() }));
    post.flush(homeworkList().current[0]);
    backend.expectOne(`${PATIENT_URL}/homework`).flush(homeworkList());
    await update();
    expect((q('hw-title') as HTMLInputElement).value).toBe('');
  });

  it('keeps the form when assigning fails', async () => {
    const { q, update, backend } = await render();
    type(q('hw-title'), 'Walk');
    type(q('hw-instructions'), 'Walk 15 minutes');
    await update();
    q('hw-save')!.click();
    const p = problem(503, 'Try again');
    backend.expectOne(`${PATIENT_URL}/homework`).flush(p.body, p.options);
    await update();
    expect(q('problem')!.textContent).toContain('Try again');
    expect((q('hw-title') as HTMLInputElement).value).toBe('Walk');
  });

  it('previews a reply before sending it to the check-in', async () => {
    const { q, update, backend } = await render();
    q('reply')!.click();
    await update();
    type(q('reply-text'), 'Well noticed.');
    await update();
    expect((q('reply-send') as HTMLButtonElement).disabled).toBeTrue();
    q('reply-preview-button')!.click();
    await update();
    expect(q('reply-preview')!.textContent).toContain('Well noticed.');
    q('reply-send')!.click();
    const post = backend.expectOne(`${API}/v1/staff/checkins/ci-1/feedback`);
    expect(post.request.body).toEqual({ text: 'Well noticed.' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    backend.expectOne(`${PATIENT_URL}/homework`).flush(homeworkList());
    await update();
  });

  it('cancels only after a confirmation', async () => {
    const { q, update, backend, overlay } = await render();
    q('cancel')!.click();
    await update();
    expect(overlay().textContent).toContain('Cancel this homework?');
    (overlay().querySelector('[data-testid="reason-submit"]') as HTMLElement).click();
    await update();
    backend.expectOne((r) => r.method === 'DELETE' && r.url === `${API}/v1/staff/homework/hw-1`).flush(null, { status: 204, statusText: 'No Content' });
    await update();
    backend.expectOne(`${PATIENT_URL}/homework`).flush(homeworkList());
  });
});

function planHistory(overrides: Partial<PlanHistory> = {}): PlanHistory {
  return {
    pseudonymId: PID,
    current: { version: 2, createdAt: '2026-10-05T08:00:00Z', authorName: 'Sara M.', goals: [{ text: 'Sleep better', status: 'ongoing' }], interventions: 'CBT-I', frequency: 'weekly', reviewOn: '2030-11-30', followUpNotes: '' },
    versions: [
      { version: 2, createdAt: '2026-10-05T08:00:00Z', authorName: 'Sara M.' },
      { version: 1, createdAt: '2026-09-21T08:00:00Z', authorName: 'Sara M.' },
    ],
    fileApproved: true,
    canEdit: true,
    ...overrides,
  };
}

describe('TreatmentPlanComponent (S15)', () => {
  it('saves a new version with If-Match and shows both versions on 412', async () => {
    const { q, update, backend } = await renderCare(TreatmentPlanComponent, { id: PID });
    backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
    await update();
    expect((q('plan-goals') as HTMLTextAreaElement).value).toBe('Sleep better');
    type(q('plan-goals'), 'Sleep better\nWalk weekly');
    await update();
    expect(q('goal-statuses')!.querySelectorAll('li').length).toBe(2);
    q('plan-save')!.click();
    const post = backend.expectOne(`${PATIENT_URL}/plans`);
    expect(post.request.method).toBe('POST');
    expect(post.request.headers.get('If-Match')).toBe('"2"');
    expect(post.request.body.goals).toEqual([{ text: 'Sleep better', status: 'ongoing' }, { text: 'Walk weekly', status: 'not_started' }]);
    const p = problem(412, 'Changed');
    post.flush(p.body, p.options);
    const newer = planHistory({ current: { ...planHistory().current!, version: 3, interventions: 'Thought record' } });
    backend.expectOne(`${PATIENT_URL}/plans`).flush(newer);
    await update();
    expect(q('plan-conflict')!.textContent).toContain('Thought record');
    expect((q('plan-goals') as HTMLTextAreaElement).value).toBe('Sleep better\nWalk weekly');
    q('plan-save')!.click();
    expect(backend.expectOne(`${PATIENT_URL}/plans`).request.headers.get('If-Match')).toBe('"3"');
  });

  it('disables saving until the clinical file is approved, with a link to it', async () => {
    const { q, update, backend } = await renderCare(TreatmentPlanComponent, { id: PID }, { language: 'ar' });
    backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory({ current: null, versions: [], fileApproved: false }));
    await update();
    expect(q('file-not-approved')!.querySelector('a')!.getAttribute('href')).toBe(`/patients/${PID}/file`);
    expect((q('plan-save') as HTMLButtonElement).disabled).toBeTrue();
    expect(q('no-plan')!.textContent).toContain('لا إصدارات بعد');
  });

  it('keeps the draft in memory when the connection drops and saves it again when it returns', async () => {
    const first = await renderCare(TreatmentPlanComponent, { id: PID });
    first.backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
    await first.update();
    type(first.q('plan-goals'), 'Sleep better\nWalk weekly');
    await first.update();
    first.q('plan-save')!.click();
    first.backend.expectOne(`${PATIENT_URL}/plans`).error(new ProgressEvent('error'));
    await first.update();
    expect(first.q('plan-waiting')!.textContent).toContain('Connection lost');

    // Leaving and coming back keeps it (memory only: nothing is written to browser storage).
    first.fixture.destroy();
    expect(JSON.stringify({ ...localStorage })).not.toContain('Walk weekly');
    expect(JSON.stringify({ ...sessionStorage })).not.toContain('Walk weekly');
    const again = TestBed.createComponent(TreatmentPlanComponent);
    again.componentRef.setInput('id', PID);
    again.detectChanges();
    const el = again.nativeElement as HTMLElement;
    first.backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
    // Back online in the meantime: the waiting save is sent at once, based on the version it started from.
    const resent = first.backend.expectOne(`${PATIENT_URL}/plans`);
    expect(resent.request.headers.get('If-Match')).toBe('"2"');
    expect(resent.request.body.goals.length).toBe(2);
    resent.flush({});
    first.backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory({ current: { ...planHistory().current!, version: 3 } }));
    again.detectChanges();
    await first.update();
    again.detectChanges();
    expect(el.querySelector('[data-testid="plan-waiting"]')).toBeNull();
    expect(TestBed.inject(SessionService).draft(planDraftKey(PID))).toBeNull();
  });

  it('sends a waiting save when the browser reports it is online again', async () => {
    const { q, update, backend } = await renderCare(TreatmentPlanComponent, { id: PID });
    backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
    await update();
    type(q('plan-interventions'), 'CBT-I, breathing');
    q('plan-save')!.click();
    backend.expectOne(`${PATIENT_URL}/plans`).error(new ProgressEvent('error'));
    await update();
    window.dispatchEvent(new Event('online'));
    const resent = backend.expectOne(`${PATIENT_URL}/plans`);
    expect(resent.request.body.interventions).toBe('CBT-I, breathing');
    resent.flush({});
    backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
  });

  it('opens an older version read-only', async () => {
    const { q, update, backend } = await renderCare(TreatmentPlanComponent, { id: PID });
    backend.expectOne(`${PATIENT_URL}/plans`).flush(planHistory());
    await update();
    q('view-1')!.click();
    backend.expectOne(`${PATIENT_URL}/plans/1`).flush({ ...planHistory().current!, version: 1, interventions: 'Breathing' });
    await update();
    expect(q('plan-viewing')!.textContent).toContain('Breathing');
    expect(q('plan-viewing')!.textContent).toContain('Read-only');
    expect(q('viewing-frequency')!.textContent).toContain('Weekly');
  });
});

function noteView(overrides: Partial<SessionNoteView> = {}): SessionNoteView {
  return {
    pseudonymId: PID,
    appointment: { id: 'ap-5', startsAt: '2026-10-20T10:00:00Z', durationMinutes: 50, sequenceNumber: 5, status: 'confirmed' },
    note: null,
    lastWeek: { homeworkDone: 8, homeworkTarget: 9, moodAverage: 6.4, alerts: 0 },
    canEdit: true,
    templates: { soap: ['s', 'o', 'a', 'p'], dap: ['d', 'a', 'p'], free: ['text'] },
    ...overrides,
  };
}

describe('SessionNotesComponent (S33)', () => {
  it('quotes the patient\'s pre-session note and books the next session from the therapist\'s free times', async () => {
    const { q, update, backend, overlay, fixture } = await renderCare(SessionNotesComponent, { id: PID, appointmentId: 'ap-5' });
    backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`).flush(noteView({ preSessionNote: 'Sleep is better, but Sunday\'s meeting worries me.', preSessionNoteAt: '2026-10-19T18:00:00Z', nextAppointment: null }));
    await update();
    expect(q('pre-session-note')!.textContent).toContain('Sunday\'s meeting worries me.');
    q('book-next')!.click();
    await update();
    backend.expectOne((r) => r.url === `${API}/v1/staff/free-slots`).flush([{ slotId: 's7', startsAt: '2026-10-27T07:00:00Z', durationMinutes: 50 }]);
    await update();
    (overlay().querySelector('[data-testid="slot-select"] .mat-mdc-select-trigger') as HTMLElement).click();
    await update();
    (overlay().querySelector('[data-testid="slot-s7"]') as HTMLElement).click();
    await update();
    (overlay().querySelector('[data-testid="slot-submit"]') as HTMLElement).click();
    const post = backend.expectOne(`${PATIENT_URL}/appointments`);
    expect(post.request.body).toEqual({ slotId: 's7' });
    post.flush({ id: 'ap-6', pseudonymId: PID, startsAt: '2026-10-27T07:00:00Z', durationMinutes: 50, sequenceNumber: 6, status: 'confirmed' });
    await update();
    expect(q('next-session')).not.toBeNull();
    expect(q('book-next')).toBeNull();
    fixture.destroy();
  });

  it('says when the patient wrote nothing before the session', async () => {
    const { q, update, backend, fixture } = await renderCare(SessionNotesComponent, { id: PID, appointmentId: 'ap-5' });
    backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`).flush(noteView());
    await update();
    expect(q('no-pre-session')!.textContent).toContain('wrote nothing');
    fixture.destroy();
  });

  it('saves a SOAP draft with revision 0, then signs it after a confirmation', async () => {
    const { q, update, backend, overlay, fixture } = await renderCare(SessionNotesComponent, { id: PID, appointmentId: 'ap-5' });
    backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`).flush(noteView());
    await update();
    expect(q('last-week')!.textContent).toContain('Homework 8 / 9');
    type(q('field-s'), 'Sleeping better.');
    await update();
    expect(q('save-state')!.textContent).toContain('Unsaved changes');
    q('note-save')!.click();
    const put = backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('If-Match')).toBe('"0"');
    expect(put.request.body.fields.s).toBe('Sleeping better.');
    const saved = noteView({ note: { id: 'n1', template: 'soap', fields: { s: 'Sleeping better.', o: '', a: '', p: '' }, summaryPoints: [], shareSummary: false, status: 'draft', revision: 1, updatedAt: '2026-10-20T10:40:00Z', signedAt: null, signedBy: null, addenda: [] } });
    put.flush(saved);
    await update();
    q('note-sign')!.click();
    await update();
    expect(overlay().textContent).toContain('Sign and lock this note?');
    (overlay().querySelector('[data-testid="reason-submit"]') as HTMLElement).click();
    await update();
    const sign = backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note/sign`);
    expect(sign.request.headers.get('If-Match')).toBe('"1"');
    sign.flush(noteView({ note: { ...saved.note!, status: 'signed', revision: 2, signedAt: '2026-10-20T10:45:00Z', signedBy: 'Sara M.' } }));
    await update();
    expect(q('note-signed')!.textContent).toContain('Sara M.');
    expect(q('note-save')).toBeNull();
    expect((q('field-s') as HTMLTextAreaElement).readOnly).toBeTrue();
    expect(q('addendum-text')).not.toBeNull();
    fixture.destroy();
  });

  it('says «not saved» and keeps the text when a save fails', async () => {
    const { q, update, backend, fixture } = await renderCare(SessionNotesComponent, { id: PID, appointmentId: 'ap-5' }, { language: 'ar' });
    backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`).flush(noteView());
    await update();
    type(q('field-p'), 'خطة');
    await update();
    q('note-save')!.click();
    const p = problem(503, 'غير متاح');
    backend.expectOne(`${PATIENT_URL}/sessions/ap-5/note`).flush(p.body, p.options);
    await update();
    expect(q('save-state')!.textContent).toContain('لم يُحفظ');
    expect((q('field-p') as HTMLTextAreaElement).value).toBe('خطة');
    fixture.destroy();
  });
});

function safetyPlan(overrides: Partial<StaffSafetyPlan> = {}): StaffSafetyPlan {
  return {
    pseudonymId: PID,
    sections: { warningSigns: 'Staying up late', copingAlone: '', distractions: '', peopleToAsk: '', professionals: '', safeEnvironment: '', reasonToLive: '' },
    contacts: [{ name: 'My sister', phone: '0551234567' }],
    version: 2,
    updatedAt: '2026-10-18T08:00:00Z',
    updatedBy: null,
    updatedByRole: 'patient',
    emergency: { emergencyNumber: '911', supportLine: '920033360', therapistName: 'Sara M.' },
    canEdit: true,
    ...overrides,
  };
}

describe('SafetyPlanBuilderComponent (S35)', () => {
  async function render() {
    const r = await renderCare(SafetyPlanBuilderComponent, { id: PID });
    r.backend.expectOne(`${PATIENT_URL}/safety-plan`).flush(safetyPlan());
    r.backend.expectOne(`${PATIENT_URL}/safety-plan/versions`).flush([{ version: 2, createdAt: '2026-10-18T08:00:00Z', authorRole: 'patient', authorName: '' }]);
    await r.update();
    return r;
  }

  it('shows the plan with the automatic step 5 lines, contacts and version', async () => {
    const { q } = await render();
    expect((q('section-warningSigns') as HTMLTextAreaElement).value).toBe('Staying up late');
    expect(q('auto-lines')!.textContent).toContain('Sara M.');
    expect(q('auto-lines')!.textContent).toContain('911');
    expect(q('contacts')!.textContent).toContain('0551234567');
    expect(q('safety-version')!.textContent).toContain('Version 2');
  });

  it('adds a person, saves with If-Match and shows both versions when the patient saved first', async () => {
    const { q, update, backend } = await render();
    q('add-person')!.click();
    await update();
    type(q('contact-name'), 'Friend Khalid');
    type(q('contact-relation'), 'Friend');
    type(q('contact-phone'), '12');
    await update();
    expect((q('contact-add') as HTMLButtonElement).disabled).toBeTrue();
    type(q('contact-phone'), '0559876543');
    await update();
    q('contact-add')!.click();
    await update();
    // The phone is optional.
    q('add-person')!.click();
    await update();
    type(q('contact-name'), 'Coach Ali');
    type(q('contact-relation'), 'Coach');
    await update();
    expect((q('contact-add') as HTMLButtonElement).disabled).toBeFalse();
    q('contact-add')!.click();
    await update();
    q('safety-save')!.click();
    const post = backend.expectOne(`${PATIENT_URL}/safety-plans`);
    expect(post.request.headers.get('If-Match')).toBe('"2"');
    expect(post.request.body.contacts).toEqual([
      { name: 'My sister', phone: '0551234567' },
      { name: 'Friend Khalid', relation: 'Friend', phone: '0559876543' },
      { name: 'Coach Ali', relation: 'Coach', phone: '' },
    ]);
    expect(q('contacts')!.textContent).toContain('Coach Ali (Coach)');
    const p = problem(412, 'Changed');
    post.flush(p.body, p.options);
    backend.expectOne(`${PATIENT_URL}/safety-plan`).flush(safetyPlan({ version: 3, sections: { ...safetyPlan().sections, copingAlone: 'A short walk' } }));
    await update();
    expect(q('safety-conflict')!.textContent).toContain('A short walk');
    q('keep-mine')!.click();
    expect(backend.expectOne(`${PATIENT_URL}/safety-plans`).request.headers.get('If-Match')).toBe('"3"');
  });
});

function closureView(overrides: Partial<ClosureView> = {}): ClosureView {
  return {
    pseudonymId: PID,
    closure: null,
    prefill: { sessionCount: 10, durationWeeks: 12 },
    measures: [{ instrument: 'PHQ-9', start: 16, end: null }, { instrument: 'GAD-7', start: 13, end: null }],
    blocked: false,
    canEdit: true,
    canSignOff: false,
    therapistName: 'Sara M.',
    ...overrides,
  };
}

describe('EndOfCareComponent (S57)', () => {
  it('blocks sending at high risk and explains why', async () => {
    const { q, update, backend } = await renderCare(EndOfCareComponent, { id: PID }, { language: 'ar' });
    backend.expectOne(`${PATIENT_URL}/closure`).flush(closureView({ blocked: true }), { headers: new HttpHeaders({ ETag: '"0"' }) });
    await update();
    expect(q('close-blocked')!.textContent).toContain('3 أو 4');
    expect((q('close-submit') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('prefills sessions, flags numbers in the patient summary and sends it for sign-off', async () => {
    const { q, update, backend, fixture } = await renderCare(EndOfCareComponent, { id: PID });
    backend.expectOne(`${PATIENT_URL}/closure`).flush(closureView(), { headers: new HttpHeaders({ ETag: '"0"' }) });
    await update();
    expect((q('close-sessions') as HTMLInputElement).value).toBe('10');
    expect(q('close-measures')!.textContent).toContain('16');
    type(q('close-patient-summary'), 'Your PHQ-9 went from 16 to 7.');
    await update();
    expect(q('close-numbers')).not.toBeNull();
    type(q('close-patient-summary'), 'You learned to notice harsh thoughts.');
    type(q('close-signs'), 'Poor sleep');
    type(q('close-todo'), 'Walk, call my sister');
    (fixture.componentInstance as unknown as { draft: { reasonCode: string } }).draft.reasonCode = 'goals_met';
    await update();
    q('close-submit')!.click();
    const put = backend.expectOne(`${PATIENT_URL}/closure`);
    expect(put.request.method).toBe('PUT');
    expect(put.request.headers.get('If-Match')).toBe('"0"');
    expect(put.request.body).toEqual(jasmine.objectContaining({ reasonCode: 'goals_met', sessionCount: 10, durationWeeks: 12, warningSigns: 'Poor sleep', submit: true }));
    const submitted = closureView({ closure: { id: 'c1', status: 'submitted', reasonCode: 'goals_met', sessionCount: 10, durationWeeks: 12, patientSummary: 'You learned to notice harsh thoughts.', warningSigns: 'Poor sleep', whatToDo: 'Walk, call my sister', followUp: 'phq9_1m_3m', keepLibrary: true, submittedAt: '2026-10-20T10:00:00Z', rejectionReason: null, decidedBy: null, decidedAt: null, revision: 2 } });
    put.flush(submitted, { headers: new HttpHeaders({ ETag: '"2"' }) });
    await update();
    expect(q('close-waiting')).not.toBeNull();
    expect(q('close-submit')).toBeNull();
    expect(q('close-withdraw')).not.toBeNull();
  });

  it('lets a supervisor sign off a submitted summary', async () => {
    const { q, update, backend, overlay } = await renderCare(EndOfCareComponent, { id: PID }, { role: 'supervisor' });
    const submitted = closureView({ canEdit: false, canSignOff: true, closure: { id: 'c1', status: 'submitted', reasonCode: 'goals_met', sessionCount: 10, durationWeeks: 12, patientSummary: 'x', warningSigns: 'y', whatToDo: 'z', followUp: 'none', keepLibrary: true, submittedAt: '2026-10-20T10:00:00Z', rejectionReason: null, decidedBy: null, decidedAt: null, revision: 2 } });
    backend.expectOne(`${PATIENT_URL}/closure`).flush(submitted, { headers: new HttpHeaders({ ETag: '"2"' }) });
    await update();
    q('close-sign-off')!.click();
    await update();
    expect(overlay().textContent).toContain('Sign off the end of care?');
    (overlay().querySelector('[data-testid="reason-submit"]') as HTMLElement).click();
    await update();
    const post = backend.expectOne(`${API}/v1/staff/closures/c1/sign-off`);
    expect(post.request.body).toEqual({ approve: true, reason: undefined });
  });

  it('knows a summary with digits in either script has numbers', () => {
    expect(HAS_NUMBERS.test('درجتك ١٦')).toBeTrue();
    expect(HAS_NUMBERS.test('تحسن واضح')).toBeFalse();
  });
});

