import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL } from '../../core/api';
import type { Schedule, StaffAppointment } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API, problem, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { ScheduleComponent, badgeOf } from './schedule.component';
import { addDays, today, weekStart, zonedParts, zonedToUtc } from './zoned-time';

const TZ = 'Asia/Riyadh';

function appointment(overrides: Partial<StaffAppointment> = {}): StaffAppointment {
  return {
    id: 'a1',
    pseudonymId: 'P-9KD2-07LM',
    startsAt: zonedToUtc(today(TZ), '11:00', TZ),
    durationMinutes: 50,
    sequenceNumber: 3,
    status: 'confirmed',
    riskLevel: 1,
    filePending: false,
    outcomeRecorded: false,
    canRecordOutcome: true,
    canCancel: false,
    canReportLate: false,
    therapistLateMinutes: null,
    cancelledBy: null,
    cancelReasonCode: null,
    missed: false,
    ...overrides,
  };
}

describe('zoned time helpers', () => {
  it('reads and writes wall-clock times in the therapist zone, whatever the browser zone', () => {
    expect(zonedToUtc('2026-10-18', '10:00', TZ)).toBe('2026-10-18T07:00:00.000Z');
    expect(zonedParts('2026-10-18T21:30:00Z', TZ)).toEqual(jasmine.objectContaining({ date: '2026-10-19', time: '00:30', day: 1 }));
    expect(weekStart('2026-10-22')).toBe('2026-10-18');
    expect(addDays('2026-10-31', 1)).toBe('2026-11-01');
  });
});

describe('badgeOf', () => {
  it('puts cancellations and missed sessions first, then the outcome to record, risk and the pending file', () => {
    expect(badgeOf(appointment({ status: 'cancelled_by_therapist', cancelledBy: 'therapist' }))).toBe('cancelled');
    expect(badgeOf(appointment({ status: 'no_show', missed: true, canRecordOutcome: false }))).toBe('missed');
    expect(badgeOf(appointment({ riskLevel: 3 }))).toBe('outcome');
    expect(badgeOf(appointment({ canRecordOutcome: false, riskLevel: 3 }))).toBe('risk');
    expect(badgeOf(appointment({ canRecordOutcome: false, filePending: true }))).toBe('file');
    expect(badgeOf(appointment({ canRecordOutcome: false }))).toBeNull();
  });
});

describe('ScheduleComponent', () => {
  let backend: HttpTestingController;

  async function render(items: StaffAppointment[], language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [ScheduleComponent],
      providers: [provideTestTranslations(), provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ScheduleComponent);
    fixture.detectChanges();
    const from = weekStart(today(TZ));
    const schedule: Schedule = { from, to: addDays(from, 6), timeZone: TZ, items };
    backend.expectOne((r) => r.url === `${API}/v1/staff/schedule` && r.params.get('from') === from && r.params.get('days') === '7').flush(schedule);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const update = async () => {
      fixture.detectChanges();
      await settle();
      fixture.detectChanges();
    };
    return { fixture, el, update };
  }

  afterEach(() => backend.verify());

  it('shows the week with each session, its number and its alert', async () => {
    const { el } = await render([appointment(), appointment({ id: 'a2', sequenceNumber: 1, startsAt: zonedToUtc(today(TZ), '15:00', TZ), riskLevel: 3, canRecordOutcome: false })]);
    expect(el.querySelector('[data-testid="appointment-a1"]')!.textContent).toContain('P-9KD2-07LM · Session 3');
    expect(el.querySelector('[data-testid="appointment-a1"]')!.textContent).toContain('Record the outcome');
    expect(el.querySelector('[data-testid="appointment-a2"]')!.textContent).toContain('First');
    expect(el.querySelector('[data-testid="appointment-a2"]')!.textContent).toContain('High risk');
    expect(el.querySelector('[data-testid="previous"]')!.getAttribute('aria-label')).toBe('Previous week');
  });

  it('says so when the week has no sessions, and moves a week at a time', async () => {
    const { el, update } = await render([]);
    expect(el.querySelector('[data-testid="schedule-empty"]')!.textContent).toContain('No sessions this week.');
    (el.querySelector('[data-testid="next"]') as HTMLButtonElement).click();
    await update();
    const next = addDays(weekStart(today(TZ)), 7);
    backend.expectOne((r) => r.url === `${API}/v1/staff/schedule` && r.params.get('from') === next).flush({ from: next, to: addDays(next, 6), timeZone: TZ, items: [] });
  });

  it('records «تمت الجلسة» and goes on to the session notes (S33)', async () => {
    const { el, update } = await render([appointment()]);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    expect(el.querySelector('[data-testid="appointment-state"]')!.textContent).toContain('The session has ended. Record the outcome.');
    (el.querySelector('[data-testid="outcome-attended"]') as HTMLButtonElement).click();
    const req = backend.expectOne(`${API}/v1/staff/appointments/a1/outcome`);
    expect(req.request.body).toEqual({ outcome: 'attended' });
    req.flush(null);
    await update();
    expect(navigate).toHaveBeenCalledWith(['/patients', 'P-9KD2-07LM', 'sessions', 'a1', 'notes']);
  });

  it('records a no-show and stays on the schedule', async () => {
    const { el, update } = await render([appointment()]);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    (el.querySelector('[data-testid="outcome-no-show"]') as HTMLButtonElement).click();
    backend.expectOne(`${API}/v1/staff/appointments/a1/outcome`).flush(null);
    await update();
    backend.expectOne((r) => r.url === `${API}/v1/staff/schedule`).flush({ from: '', to: '', timeZone: TZ, items: [appointment({ status: 'no_show', missed: true })] });
  });

  it('links a past session to its notes, and not a cancelled one', async () => {
    const { el, update } = await render([
      appointment({ status: 'completed', outcomeRecorded: true, canRecordOutcome: false }),
      appointment({ id: 'a2', startsAt: zonedToUtc(today(TZ), '13:00', TZ), status: 'cancelled_by_patient', cancelledBy: 'patient' }),
    ]);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    expect(el.querySelector('[data-testid="session-notes"]')!.getAttribute('href')).toBe('/patients/P-9KD2-07LM/sessions/a1/notes');
    (el.querySelector('[data-testid="appointment-a2"]') as HTMLButtonElement).click();
    await update();
    expect(el.querySelector('[data-testid="session-notes"]')).toBeNull();
  });

  it('reschedules to one of the therapist\'s free times with a reason', async () => {
    const future = zonedToUtc(addDays(today(TZ), 1), '11:00', TZ);
    const { el, update } = await render([appointment({ startsAt: future, canRecordOutcome: false, canCancel: true })]);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    (el.querySelector('[data-testid="reschedule"]') as HTMLButtonElement).click();
    await update();
    backend.expectOne((r) => r.url === `${API}/v1/staff/free-slots`).flush([{ slotId: 's9', startsAt: zonedToUtc(addDays(today(TZ), 2), '10:00', TZ), durationMinutes: 50 }]);
    await update();
    const dialog = document.querySelector('tm-slot-picker-dialog') as HTMLElement;
    (dialog.querySelector('[data-testid="slot-submit"]') as HTMLButtonElement).click();
    await update();
    backend.expectNone(`${API}/v1/staff/appointments/a1/reschedule`);
    (dialog.querySelector('[data-testid="slot-select"] .mat-mdc-select-trigger') as HTMLElement).click();
    await update();
    (document.querySelector('[data-testid="slot-s9"]') as HTMLElement).click();
    await update();
    (dialog.querySelector('[data-testid="slot-reason"] .mat-mdc-select-trigger') as HTMLElement).click();
    await update();
    (document.querySelector('[data-testid="reason-illness"]') as HTMLElement).click();
    await update();
    (dialog.querySelector('[data-testid="slot-submit"]') as HTMLButtonElement).click();
    const req = backend.expectOne(`${API}/v1/staff/appointments/a1/reschedule`);
    expect(req.request.body).toEqual({ slotId: 's9', reasonCode: 'illness' });
    req.flush(appointment({ id: 'a1' }));
    await update();
    backend.expectOne((r) => r.url === `${API}/v1/staff/schedule`).flush({ from: '', to: '', timeZone: TZ, items: [] });
  });

  it('keeps outcome buttons disabled before the start, and offers late, cancel and start while the session is on', async () => {
    const { el, update } = await render([
      appointment({ canRecordOutcome: false, canCancel: true }),
      appointment({ id: 'a2', startsAt: zonedToUtc(today(TZ), '13:00', TZ), canReportLate: true, canCancel: true }),
    ]);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    expect((el.querySelector('[data-testid="outcome-attended"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((el.querySelector('[data-testid="outcome-no-show"]') as HTMLButtonElement).disabled).toBeTrue();
    expect(el.querySelector('[data-testid="running-late"]')).toBeNull();
    expect(el.querySelector('[data-testid="cancel"]')).not.toBeNull();

    (el.querySelector('[data-testid="appointment-a2"]') as HTMLButtonElement).click();
    await update();
    expect(el.querySelector('[data-testid="running-late"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="start-session"]')!.getAttribute('href')).toBe('/sessions/a2');
  });

  it('shows the server problem and refreshes when the session changed meanwhile', async () => {
    const { el, update } = await render([appointment()]);
    (el.querySelector('[data-testid="appointment-a1"]') as HTMLButtonElement).click();
    await update();
    (el.querySelector('[data-testid="outcome-no-show"]') as HTMLButtonElement).click();
    const { body, options } = problem(409, 'This appointment can no longer be changed.');
    backend.expectOne(`${API}/v1/staff/appointments/a1/outcome`).flush(body, options);
    await update();
    expect(el.querySelector('[data-testid="problem"]')!.textContent).toContain('This appointment can no longer be changed.');
    backend.expectOne((r) => r.url === `${API}/v1/staff/schedule`).flush({ from: '', to: '', timeZone: TZ, items: [appointment()] });
  });

  it('renders in Arabic', async () => {
    const { el } = await render([appointment({ sequenceNumber: 1, canRecordOutcome: false, filePending: true })], 'ar');
    expect(el.querySelector('h1')!.textContent).toContain('الجدول');
    expect(el.querySelector('[data-testid="appointment-a1"]')!.textContent).toContain('الملف بانتظار اعتمادك');
  });
});
