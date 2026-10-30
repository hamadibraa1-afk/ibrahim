import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { ActivatedRoute, type ParamMap, Router, convertToParamMap } from '@angular/router';
import { BehaviorSubject } from 'rxjs';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { CrisisEventDetail, CrisisHealth } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { FakeCrisisHub } from '../../testing/fake-crisis-hub';
import { API, CRISIS, eventDetail, openEvent } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { SessionService } from '../../core/session/session.service';
import { CrisisConsoleComponent, TEL_OPENER, telHref } from './crisis-console.component';
import { localToIso } from './crisis-dialogs';

const HEALTH: CrisisHealth = { gate: 'ok', classifier: 'degraded', comms: 'down', onCall: 'Sara A.', backup: null };

describe('CrisisConsoleComponent', () => {
  let backend: HttpTestingController;
  let hub: FakeCrisisHub;
  let fixture: ComponentFixture<CrisisConsoleComponent>;
  let el: HTMLElement;
  let params: BehaviorSubject<ParamMap>;
  let navigate: jasmine.Spy;
  let opened: string[];

  const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
  const dialog = () => overlay().querySelector('mat-dialog-container') as HTMLElement | null;
  const update = () => {
    fixture.detectChanges();
    flush();
    fixture.detectChanges();
  };
  const type = (input: HTMLInputElement | HTMLTextAreaElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  /** Renders the console with one open event 90 s before its acknowledgement deadline. */
  function render(options: { health?: CrisisHealth; detail?: Partial<CrisisEventDetail>; eventId?: string } = {}) {
    const health = options.health ?? HEALTH;
    params = new BehaviorSubject(convertToParamMap(options.eventId ? { eventId: options.eventId } : {}));
    navigate = jasmine.createSpy('navigate').and.resolveTo(true);
    opened = [];
    TestBed.configureTestingModule({
      imports: [CrisisConsoleComponent],
      providers: [
        provideTestTranslations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: API_BASE_URL, useValue: API },
        { provide: CRISIS_BASE_URL, useValue: CRISIS },
        { provide: CrisisHubService, useClass: FakeCrisisHub },
        { provide: ActivatedRoute, useValue: { paramMap: params.asObservable() } },
        { provide: Router, useValue: { navigate } },
        { provide: TEL_OPENER, useValue: (href: string) => opened.push(href) },
      ],
    });
    void TestBed.inject(LanguageService).use('en');
    flushMicrotasks();
    backend = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(CrisisHubService) as unknown as FakeCrisisHub;
    fixture = TestBed.createComponent(CrisisConsoleComponent);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
    const ev = openEvent({ ackDeadlineUtc: new Date(Date.now() + 90_000).toISOString() });
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([ev]);
    backend.expectOne(`${CRISIS}/v1/crisis/health`).flush(health);
    fixture.detectChanges();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ ackDeadlineUtc: ev.ackDeadlineUtc, ...options.detail }));
    fixture.detectChanges();
  }

  const button = (testId: string) => el.querySelector(`[data-testid="${testId}"]`) as HTMLButtonElement;
  const inDialog = (testId: string) => dialog()!.querySelector(`[data-testid="${testId}"]`) as HTMLElement;
  const OWNED: Partial<CrisisEventDetail> = { status: 'Acknowledged', acknowledgedBy: 'Sara A.', isOwner: true };

  function finish() {
    fixture.destroy();
    discardPeriodicTasks();
    flush();
    backend.verify();
    localStorage.clear();
  }

  it('lists open events with the count, number, level, pseudonym, source and ack state', fakeAsync(() => {
    render();
    expect(el.querySelector('#open-events-title')!.textContent).toContain('Open events · 1');
    const item = el.querySelector('[data-testid="event"]')!;
    expect(item.textContent).toContain('CE-1042 · High');
    expect(item.textContent).toContain('P-3HX9-14QA');
    expect(item.textContent).toContain('Intake chat');
    expect(item.textContent).toContain('Not acknowledged');
    expect(el.querySelector('[data-testid="detail-meta"]')!.textContent).toContain('Rules CR-1');
    expect(el.querySelector('[data-testid="detail-meta"]')!.textContent).toContain('Rule pack en-1.4');
    expect(el.querySelector('[data-testid="timeline"]')!.textContent).toContain('Event opened automatically');
    expect(el.querySelector('[data-testid="masked-contact"]')!.textContent).toContain('••• ••• ••21');
    expect(el.querySelector('[data-testid="masked-contact"]')!.textContent).toContain('Emergency contact: registered');
    expect(hub.users).toBe(1);
    finish();
  }));

  it('counts down to the acknowledgement deadline, turns red under a minute and announces it once', fakeAsync(() => {
    render();
    const countdown = () => el.querySelector('[data-testid="event-countdown"]') as HTMLElement;
    const announcer = () => el.querySelector('[data-testid="announcer"]')!.textContent!.trim();
    tick(1000);
    fixture.detectChanges();
    expect(countdown().textContent).toBe('01:29');
    expect(countdown().classList).not.toContain('red');
    expect(el.querySelector('[data-testid="sla"]')!.classList).not.toContain('red');
    expect(announcer()).toBe('');

    tick(30_000);
    fixture.detectChanges();
    backend.match(`${CRISIS}/v1/crisis/health`).forEach((r) => r.flush(HEALTH));
    expect(countdown().textContent).toBe('00:59');
    expect(countdown().classList).toContain('red');
    expect(el.querySelector('[data-testid="sla"]')!.classList).toContain('red');
    expect(announcer()).toBe('CE-1042: one minute left to acknowledge.');
    finish();
  }));

  it('shows system health chips and who is on call', fakeAsync(() => {
    render();
    expect(el.querySelector('[data-testid="health-gate"]')!.getAttribute('data-state')).toBe('ok');
    expect(el.querySelector('[data-testid="health-gate"]')!.textContent).toContain('Safety gate running');
    expect(el.querySelector('[data-testid="health-classifier"]')!.textContent).toContain('Classifier degraded');
    expect(el.querySelector('[data-testid="health-comms"]')!.classList).toContain('down');
    expect(el.querySelector('[data-testid="oncall"]')!.textContent).toContain('On call: Sara A. · backup: not assigned');
    finish();
  }));

  it('needs the authenticator code to show what the patient wrote, and hides it again on time', fakeAsync(() => {
    render();
    expect(el.querySelector('[data-testid="excerpt"]')).toBeNull();
    (el.querySelector('[data-testid="reveal-excerpt"]') as HTMLButtonElement).click();
    update();
    const code = dialog()!.querySelector('[data-testid="step-up-code"]') as HTMLInputElement;
    type(code, '12');
    (dialog()!.querySelector('[data-testid="step-up-submit"]') as HTMLButtonElement).click();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/excerpt`);

    type(code, '123456');
    (dialog()!.querySelector('[data-testid="step-up-submit"]') as HTMLButtonElement).click();
    const wrong = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/excerpt`);
    expect(wrong.request.body).toEqual({ totpCode: '123456' });
    wrong.flush({ title: 'The code is not valid', status: 400 }, { status: 400, statusText: 'Bad Request' });
    update();
    expect(dialog()!.querySelector('[data-testid="problem"]')!.textContent).toContain('The code is not valid');

    type(dialog()!.querySelector('[data-testid="step-up-code"]') as HTMLInputElement, '654321');
    (dialog()!.querySelector('[data-testid="step-up-submit"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/excerpt`).flush({ excerpt: 'I have a plan for tonight.', hideAt: new Date(Date.now() + 20_000).toISOString() });
    update();
    expect(dialog()).toBeNull();
    expect(el.querySelector('[data-testid="excerpt"]')!.textContent).toContain('I have a plan for tonight.');

    tick(21_000);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="excerpt"]')).toBeNull();
    expect(el.querySelector('[data-testid="reveal-excerpt"]')).not.toBeNull();
    finish();
  }));

  it('reveals contact data with code and reason, enables calling, and masks it again after 10 minutes', fakeAsync(() => {
    render();
    expect((el.querySelector('[data-testid="call"]') as HTMLButtonElement).disabled).toBeTrue();
    (el.querySelector('[data-testid="reveal-contact"]') as HTMLButtonElement).click();
    update();
    type(dialog()!.querySelector('[data-testid="step-up-code"]') as HTMLInputElement, '123456');
    (dialog()!.querySelector('[data-testid="step-up-submit"]') as HTMLButtonElement).click();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/reveal-contact`);

    type(dialog()!.querySelector('[data-testid="step-up-reason"]') as HTMLTextAreaElement, 'Patient not answering in chat');
    (dialog()!.querySelector('[data-testid="step-up-submit"]') as HTMLButtonElement).click();
    const reveal = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/reveal-contact`);
    expect(reveal.request.body).toEqual({ totpCode: '123456', reason: 'Patient not answering in chat' });
    reveal.flush({ contactPhone: '+966 50 000 0021', trustedName: 'Mona', trustedPhone: '+966 55 111 2222', locationConsent: true, hideAt: new Date(Date.now() + 600_000).toISOString() });
    update();

    const call = el.querySelector('[data-testid="call"]') as HTMLAnchorElement;
    expect(call.tagName).toBe('A');
    expect(call.getAttribute('href')).toBe('tel:+966500000021');
    expect(el.querySelector('[data-testid="revealed-contact"]')!.textContent).toContain('Mona');
    expect(el.querySelector('[data-testid="contact-hides"]')!.textContent).toContain('10:00');

    tick(600_000);
    fixture.detectChanges();
    backend.match(`${CRISIS}/v1/crisis/health`).forEach((r) => r.flush(HEALTH));
    expect(el.querySelector('[data-testid="revealed-contact"]')).toBeNull();
    expect((el.querySelector('[data-testid="call"]') as HTMLButtonElement).tagName).toBe('BUTTON');
    finish();
  }));

  it('validates the close dialog: outcome and reason are required', fakeAsync(() => {
    render();
    (el.querySelector('[data-testid="close-case"]') as HTMLButtonElement).click();
    update();
    const submit = () => (dialog()!.querySelector('[data-testid="close-submit"]') as HTMLButtonElement).click();
    submit();
    update();
    expect(dialog()!.querySelector('[data-testid="outcome-error"]')!.textContent).toContain('Choose an outcome');
    expect(dialog()!.querySelector('[data-testid="reason-error"]')).not.toBeNull();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/close`);

    (dialog()!.querySelector('[data-testid="close-false_alarm"] input') as HTMLInputElement).click();
    type(dialog()!.querySelector('[data-testid="close-reason"]') as HTMLTextAreaElement, '   ');
    submit();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/close`);

    type(dialog()!.querySelector('[data-testid="close-reason"]') as HTMLTextAreaElement, 'Quote from a song, confirmed with the patient');
    submit();
    const close = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/close`);
    expect(close.request.body).toEqual({ outcome: 'false_alarm', reason: 'Quote from a song, confirmed with the patient' });
    close.flush(openEvent({ status: 'Closed', kind: 'closed' }));
    update();
    expect(dialog()).toBeNull();
    expect(el.querySelectorAll('[data-testid="event"]').length).toBe(0);
    expect(el.querySelector('[data-testid="no-events"]')).not.toBeNull();
    finish();
  }));

  it('sends the patient-facing follow-up call time with the close (P38)', fakeAsync(() => {
    render();
    (el.querySelector('[data-testid="close-case"]') as HTMLButtonElement).click();
    update();
    expect(dialog()!.textContent).toContain('What you agreed with the patient (optional)');
    (dialog()!.querySelector('[data-testid="close-safe_contacted"] input') as HTMLInputElement).click();
    type(dialog()!.querySelector('[data-testid="close-reason"]') as HTMLTextAreaElement, 'Reached by phone, safe');
    type(dialog()!.querySelector('[data-testid="close-follow-up-call"]') as HTMLInputElement, '2030-01-02T11:00');
    (dialog()!.querySelector('[data-testid="close-submit"]') as HTMLButtonElement).click();
    const close = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/close`);
    expect(close.request.body).toEqual({ outcome: 'safe_contacted', reason: 'Reached by phone, safe', followUpCallAt: new Date('2030-01-02T11:00').toISOString() });
    close.flush(openEvent({ status: 'Closed', kind: 'closed' }));
    update();
    finish();
  }));

  it('turns a datetime-local value into UTC and ignores an empty one', () => {
    expect(localToIso('')).toBeUndefined();
    expect(localToIso('not a date')).toBeUndefined();
    expect(localToIso('2030-01-02T11:00')).toBe(new Date('2030-01-02T11:00').toISOString());
  });

  it('hides "close with outcome" when the event cannot be closed', fakeAsync(() => {
    render();
    hub.alertsSubject.next(openEvent({ kind: 'updated' }));
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ canClose: false }));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="close-case"]')).toBeNull();
    finish();
  }));

  it('acknowledges and takes the case, and shows a 409 when someone was faster', fakeAsync(() => {
    render();
    (el.querySelector('[data-testid="take"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`).flush({ title: 'Already taken', detail: 'Dr Fahad R. acknowledged it.', status: 409 }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="problem"]')!.textContent).toContain('Dr Fahad R. acknowledged it.');

    (el.querySelector('[data-testid="take"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`).flush(openEvent({ status: 'Acknowledged', kind: 'acknowledged' }));
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ status: 'Acknowledged', acknowledgedBy: 'Sara A.' }));
    update();
    expect(el.querySelector('[data-testid="take"]')).toBeNull();
    expect(el.textContent).toContain('Acknowledged by Sara A.');
    finish();
  }));

  it('records contact with a note, a free note, and the local emergency procedure with its script and number', fakeAsync(() => {
    render({ detail: OWNED });
    button('contacted').click();
    update();
    expect(dialog()!.textContent).toContain('in contact');
    (inDialog('note-submit') as HTMLButtonElement).click();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    type(inDialog('note-text') as HTMLTextAreaElement, 'Reached by phone, safe with her sister');
    (inDialog('note-submit') as HTMLButtonElement).click();
    update();
    const contacted = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(contacted.request.body).toEqual({ kind: 'contacted', text: 'Reached by phone, safe with her sister' });
    contacted.flush(eventDetail({ ...OWNED, status: 'InContact', timeline: [{ at: new Date().toISOString(), kind: 'contacted', text: 'Contact with the patient made', actor: 'Sara A.' }] }));
    update();
    expect(el.querySelector('[data-testid="timeline"]')!.textContent).toContain('Contact with the patient made');
    expect(el.querySelector('[data-testid="owner-tag"]')!.textContent).toContain('In contact with Sara A.');
    expect(button('contacted').disabled).toBeTrue();
    expect(el.querySelector('[data-testid="event"]')!.textContent).toContain('In contact');

    button('add-note').click();
    update();
    expect(inDialog('note-count').textContent).toContain('0 / 2000');
    expect(inDialog('note-text').getAttribute('maxlength')).toBe('2000');
    type(inDialog('note-text') as HTMLTextAreaElement, ' Calmer now ');
    (inDialog('note-submit') as HTMLButtonElement).click();
    update();
    const note = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(note.request.body).toEqual({ kind: 'note', text: 'Calmer now' });
    note.flush(eventDetail(OWNED));
    update();

    button('local-emergency').click();
    update();
    expect(inDialog('emergency-script').textContent).toContain('Call emergency services on 997');
    expect(inDialog('emergency-number').getAttribute('href')).toBe('tel:997');
    type(inDialog('note-text') as HTMLTextAreaElement, 'Called 997, report 5521');
    (inDialog('note-submit') as HTMLButtonElement).click();
    update();
    expect(backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`).request.body).toEqual({ kind: 'local_emergency', text: 'Called 997, report 5521' });
    finish();
  }));

  it('asks why there is no location before recording the local emergency procedure without one', fakeAsync(() => {
    render({ detail: { ...OWNED, locationConsent: false, emergencyScript: 'No location is on file for this patient. Ask during the call.' } });
    button('local-emergency').click();
    update();
    expect(dialog()!.textContent).toContain('No location is on file for this patient');
    expect(dialog()!.textContent).toContain('Why there is no location');
    finish();
  }));

  it('logs a call attempt, opens the phone, asks for the outcome and offers to record contact when answered', fakeAsync(() => {
    render({ detail: OWNED });
    button('reveal-contact').click();
    update();
    type(inDialog('step-up-code') as HTMLInputElement, '123456');
    type(inDialog('step-up-reason') as HTMLTextAreaElement, 'Calling her');
    (inDialog('step-up-submit') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/reveal-contact`).flush({ contactPhone: '0551234521', trustedName: null, trustedPhone: null, locationConsent: true, hideAt: new Date(Date.now() + 600_000).toISOString() });
    update();

    button('call').click();
    const attempt = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(attempt.request.body).toEqual({ kind: 'call_attempt' });
    expect(opened).toEqual([]);
    attempt.flush(eventDetail({ ...OWNED, recentCallAttempts: 1 }));
    update();
    expect(opened).toEqual(['tel:0551234521']);
    expect(dialog()!.textContent).toContain('How did the call go?');
    (inDialog('call-outcome-submit') as HTMLButtonElement).click();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    (dialog()!.querySelector('[data-testid="call-answered"] input') as HTMLInputElement).click();
    (inDialog('call-outcome-submit') as HTMLButtonElement).click();
    update();
    const outcome = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(outcome.request.body).toEqual({ kind: 'call_outcome', outcome: 'answered' });
    outcome.flush(eventDetail(OWNED));
    update();
    expect(dialog()!.textContent).toContain('Contact made with the patient');
    finish();
  }));

  it('warns after three unanswered calls (EC-33)', fakeAsync(() => {
    render({ detail: { ...OWNED, recentCallAttempts: 3, callAdvice: 'unreachable' } });
    expect(el.querySelector('[data-testid="call-advice"]')!.textContent).toContain('3 calls in 15 minutes without an answer');
    expect(el.querySelector('[data-testid="call-advice"]')!.getAttribute('role')).toBe('alert');
    finish();
  }));

  it('is read-only for someone who does not own the event', fakeAsync(() => {
    render({ detail: { status: 'Acknowledged', acknowledgedBy: 'Dr. Fahad R.', canAct: false, canClose: false } });
    expect(el.querySelector('[data-testid="read-only"]')!.textContent).toContain('Already acknowledged by Dr. Fahad R.');
    for (const id of ['contacted', 'local-emergency', 'add-note', 'handover']) {
      expect(button(id).disabled).withContext(id).toBeTrue();
    }
    finish();
  }));

  it('hands over to a chosen colleague, who must accept before owning the case', fakeAsync(() => {
    render({ detail: OWNED });
    button('handover').click();
    update();
    backend.expectOne(`${CRISIS}/v1/crisis/responders`).flush([
      { userId: 'u-2', displayName: 'Khalid A.', role: 'therapist', onCall: true },
      { userId: 'u-3', displayName: 'Dr. Fahad R.', role: 'supervisor', onCall: false },
    ]);
    update();
    expect(dialog()!.textContent).toContain('Khalid A.');
    (inDialog('handover-submit') as HTMLButtonElement).click();
    update();
    expect(inDialog('handover-to-error').textContent).toContain('Choose a colleague');
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/handover`);

    (dialog()!.querySelector('[data-testid="handover-u-2"] input') as HTMLInputElement).click();
    type(inDialog('handover-note') as HTMLTextAreaElement, 'Prefers a call after 9');
    (inDialog('handover-submit') as HTMLButtonElement).click();
    const offer = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/handover`);
    expect(offer.request.body).toEqual({ toUserId: 'u-2', note: 'Prefers a call after 9' });
    offer.flush(eventDetail({ ...OWNED, pendingHandover: { toUserId: 'u-2', toName: 'Khalid A.', fromName: 'Sara A.', requestedAtUtc: new Date().toISOString(), forMe: false } }));
    update();
    expect(dialog()).toBeNull();
    expect(el.querySelector('[data-testid="pending-handover"]')!.textContent).toContain('Waiting for Khalid A. to accept');
    expect(button('handover').disabled).toBeTrue();
    finish();
  }));

  it('lets the colleague asked accept the handover', fakeAsync(() => {
    render({ detail: { status: 'Acknowledged', acknowledgedBy: 'Sara A.', canAct: false, pendingHandover: { toUserId: 'me', toName: 'Khalid A.', fromName: 'Sara A.', requestedAtUtc: new Date().toISOString(), forMe: true } } });
    expect(el.querySelector('[data-testid="pending-handover"]')!.textContent).toContain('Sara A. offered you this case');
    button('accept-handover').click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/handover/accept`).flush(eventDetail({ status: 'Acknowledged', acknowledgedBy: 'Khalid A.', isOwner: true }));
    update();
    expect(el.querySelector('[data-testid="pending-handover"]')).toBeNull();
    expect(el.textContent).toContain('Acknowledged by Khalid A.');
    finish();
  }));

  it('sends a supervisor\'s unreachable close to the clinical lead, who co-signs it', fakeAsync(() => {
    render({ detail: OWNED });
    TestBed.inject(SessionService).setRoles(['supervisor']);
    button('close-case').click();
    update();
    (dialog()!.querySelector('[data-testid="close-unreachable_referred"] input') as HTMLInputElement).click();
    update();
    expect(inDialog('cosign-hint').textContent).toContain("clinical lead's co-sign");
    type(inDialog('close-reason') as HTMLTextAreaElement, 'Three calls, no answer');
    (inDialog('close-submit') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/close`).flush(openEvent({ status: 'Acknowledged', kind: 'updated' }));
    update();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ ...OWNED, canClose: false, pendingClose: { outcome: 'unreachable_referred', reason: 'Three calls, no answer', byName: 'Dr. Fahad R.', requestedAtUtc: new Date().toISOString() } }));
    update();
    expect(el.querySelectorAll('[data-testid="event"]').length).toBe(1);
    expect(el.querySelector('[data-testid="pending-close"]')!.textContent).toContain("Waiting for the clinical lead's co-sign");
    expect(el.querySelector('[data-testid="cosign"]')).toBeNull();
    finish();
  }));

  it('lets the clinical lead co-sign a waiting close', fakeAsync(() => {
    render({ detail: { canCoSign: true, canClose: false, pendingClose: { outcome: 'unreachable_referred', reason: 'No answer', byName: 'Dr. Fahad R.', requestedAtUtc: new Date().toISOString() } } });
    button('cosign').click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/close/cosign`).flush(openEvent({ status: 'Closed', kind: 'closed' }));
    update();
    expect(el.querySelectorAll('[data-testid="event"]').length).toBe(0);
    finish();
  }));

  it('opens the event in the URL (the paging link) and puts a clicked event in the URL', fakeAsync(() => {
    render({ eventId: 'ev-1' });
    expect(el.querySelector('[data-testid="event"]')!.getAttribute('aria-current')).toBe('true');
    button('event').click();
    expect(navigate).toHaveBeenCalledWith(['/crisis', 'ev-1'], { replaceUrl: true });

    params.next(convertToParamMap({ eventId: 'ev-9' }));
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-9`).flush(eventDetail({ eventId: 'ev-9', number: 'CE-1099' }));
    update();
    expect(el.querySelector('.number')!.textContent).toContain('CE-1099');
    finish();
  }));

  it('reloads everything in the new language after a switch', fakeAsync(() => {
    render();
    void TestBed.inject(LanguageService).use('ar');
    flushMicrotasks();
    fixture.detectChanges();
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([openEvent({ levelLabel: 'عالٍ' })]);
    backend.expectOne(`${CRISIS}/v1/crisis/health`).flush(HEALTH);
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ levelLabel: 'عالٍ' }));
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="event"]')!.textContent).toContain('عالٍ');
    finish();
  }));
});

describe('telHref', () => {
  it('keeps only digits and the leading plus', () => {
    expect(telHref('+966 50-000 (0021)')).toBe('tel:+966500000021');
  });
});
