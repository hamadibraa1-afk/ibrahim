import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { JoinDescriptor, SessionState, TherapistSessionPage } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API, CRISIS, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { SessionComponent, clockText, safeRoomUrl } from './session.component';

const ID = 'appt-1';
const BASE = `${API}/v1/staff/appointments/${ID}`;

function state(overrides: Partial<SessionState> = {}): SessionState {
  const now = Date.now();
  return {
    appointmentId: ID,
    serverNow: new Date(now).toISOString(),
    startsAt: new Date(now + 5 * 60_000).toISOString(),
    durationMinutes: 50,
    opensAt: new Date(now - 5 * 60_000).toISOString(),
    closesAt: new Date(now + 55 * 60_000).toISOString(),
    open: true,
    status: 'confirmed',
    peer: { present: false, joinedAt: null, lastSeenAt: null },
    therapistLate: false,
    lateOptionsAfterMinutes: 15,
    heartbeatSeconds: 15,
    provider: 'dev',
    patientCrisisOpen: false,
    ...overrides,
  };
}

function page(overrides: Partial<TherapistSessionPage> = {}): TherapistSessionPage {
  return { appointmentId: ID, pseudonymId: 'P-7Q4K-82XM', sequenceNumber: 2, riskLevel: 2, state: state(), preSessionNote: 'Sleep is better, still worried about Sunday.', preSessionNoteAt: null, ...overrides };
}

const descriptor: JoinDescriptor = {
  provider: 'dev',
  mode: 'test_room',
  url: null,
  token: 'pass-1',
  expiresAt: new Date(Date.now() + 15 * 60_000).toISOString(),
  opensAt: '',
  closesAt: new Date(Date.now() + 55 * 60_000).toISOString(),
  participant: 'therapist',
  heartbeatSeconds: 15,
};

describe('SessionComponent (therapist session page)', () => {
  let backend: HttpTestingController;

  async function render(p: TherapistSessionPage, language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [SessionComponent],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: API_BASE_URL, useValue: API },
        { provide: CRISIS_BASE_URL, useValue: CRISIS },
      ],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(SessionComponent);
    fixture.componentRef.setInput('appointmentId', ID);
    fixture.detectChanges();
    backend.expectOne(`${BASE}/session`).flush(p);
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

  it('shows the lobby with the pre-session note and the patient presence, and joins into the labelled test room', async () => {
    const { el, update } = await render(page());
    expect(el.querySelector('[data-testid="session-pseudonym"]')!.textContent).toContain('P-7Q4K-82XM');
    expect(el.querySelector('[data-testid="pre-session-note"]')!.textContent).toContain('still worried about Sunday');
    expect(el.querySelector('[data-testid="patient-presence"]')!.textContent).toContain("The patient hasn't joined yet");
    const join = el.querySelector('[data-testid="join"]') as HTMLButtonElement;
    expect(join.disabled).toBeFalse();
    join.click();
    backend.expectOne(`${BASE}/join`).flush(descriptor);
    const beat = backend.expectOne(`${BASE}/presence`);
    expect(beat.request.method).toBe('POST');
    expect(beat.request.body).toEqual({ token: 'pass-1' });
    beat.flush(state({ peer: { present: true, joinedAt: new Date().toISOString(), lastSeenAt: null } }));
    await update();
    expect(el.querySelector('[data-testid="test-room"]')!.textContent).toContain('Test room');
    expect(el.querySelector('[data-testid="patient-tile"]')!.textContent).toContain('The patient is in the session room');

    (el.querySelector('[data-testid="leave"]') as HTMLButtonElement).click();
    const leaving = backend.expectOne(`${BASE}/presence`);
    expect(leaving.request.body).toEqual({ token: 'pass-1', leaving: true });
    leaving.flush(state());
    await update();
    expect(el.querySelector('[data-testid="test-room"]')).toBeNull();
  });

  it('keeps the join closed before the window and flags an open crisis event (Arabic)', async () => {
    const now = Date.now();
    const { el } = await render(
      page({ preSessionNote: null, state: state({ opensAt: new Date(now + 60 * 60_000).toISOString(), startsAt: new Date(now + 70 * 60_000).toISOString(), patientCrisisOpen: true }) }),
      'ar',
    );
    expect((el.querySelector('[data-testid="join"]') as HTMLButtonElement).disabled).toBeTrue();
    expect(el.querySelector('[data-testid="join-hint"]')!.textContent).toContain('تفتح الغرفة قبل البدء بـ 10 دقائق');
    expect(el.querySelector('[data-testid="no-note"]')).not.toBeNull();
    expect(el.querySelector('[data-testid="crisis-open"]')!.textContent).toContain('لدى هذا المريض حدث أزمة مفتوح.');
    expect(el.querySelector('[data-testid="countdown"]')!.textContent).toMatch(/^1:09:5\d|1:10:00$/);
  });

});

describe('session page helpers', () => {
  it('formats clocks and frames only https rooms', () => {
    expect(clockText(6 * 60_000 + 42_000)).toBe('06:42');
    expect(clockText(-1)).toBe('00:00');
    expect(safeRoomUrl('https://video.example/r')).toBe('https://video.example/r');
    expect(safeRoomUrl('http://video.example/r')).toBeNull();
  });
});
