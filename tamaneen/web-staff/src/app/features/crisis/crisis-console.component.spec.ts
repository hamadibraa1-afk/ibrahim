import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { CrisisHealth } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { FakeCrisisHub } from '../../testing/fake-crisis-hub';
import { API, CRISIS, eventDetail, openEvent } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { CrisisConsoleComponent, telHref } from './crisis-console.component';

const HEALTH: CrisisHealth = { gate: 'ok', classifier: 'degraded', comms: 'down', onCall: 'Sara A.', backup: null };

describe('CrisisConsoleComponent', () => {
  let backend: HttpTestingController;
  let hub: FakeCrisisHub;
  let fixture: ComponentFixture<CrisisConsoleComponent>;
  let el: HTMLElement;

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
  function render(health: CrisisHealth = HEALTH) {
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
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1`).flush(eventDetail({ ackDeadlineUtc: ev.ackDeadlineUtc }));
    fixture.detectChanges();
  }

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
    close.flush(null, { status: 204, statusText: 'No Content' });
    update();
    expect(dialog()).toBeNull();
    expect(el.querySelectorAll('[data-testid="event"]').length).toBe(0);
    expect(el.querySelector('[data-testid="no-events"]')).not.toBeNull();
    finish();
  }));

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

  it('records actions: contacted, a note, and the local emergency procedure with its note', fakeAsync(() => {
    render();
    (el.querySelector('[data-testid="contacted"]') as HTMLButtonElement).click();
    const contacted = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(contacted.request.body).toEqual({ kind: 'contacted' });
    contacted.flush(eventDetail({ timeline: [{ at: new Date().toISOString(), kind: 'contacted', text: 'Patient reached by phone', actor: 'Sara A.' }] }));
    update();
    expect(el.querySelector('[data-testid="timeline"]')!.textContent).toContain('Patient reached by phone');

    (el.querySelector('[data-testid="add-note"]') as HTMLButtonElement).click();
    update();
    (dialog()!.querySelector('[data-testid="note-submit"]') as HTMLButtonElement).click();
    update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    type(dialog()!.querySelector('[data-testid="note-text"]') as HTMLTextAreaElement, ' Calmer now ');
    (dialog()!.querySelector('[data-testid="note-submit"]') as HTMLButtonElement).click();
    update();
    const note = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(note.request.body).toEqual({ kind: 'note', text: 'Calmer now' });
    note.flush(eventDetail());
    update();

    (el.querySelector('[data-testid="local-emergency"]') as HTMLButtonElement).click();
    update();
    type(dialog()!.querySelector('[data-testid="note-text"]') as HTMLTextAreaElement, 'Called 997');
    (dialog()!.querySelector('[data-testid="note-submit"]') as HTMLButtonElement).click();
    update();
    expect(backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`).request.body).toEqual({ kind: 'local_emergency', text: 'Called 997' });
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
