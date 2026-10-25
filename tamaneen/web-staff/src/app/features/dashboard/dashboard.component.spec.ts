import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed, discardPeriodicTasks, fakeAsync, flush, flushMicrotasks, tick } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { FakeCrisisHub } from '../../testing/fake-crisis-hub';
import { API, CRISIS, dashboard, eventDetail, openEvent, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import type { OpenCrisisEvent, TherapistDashboard } from '../../core/api/types';
import { ALERT_POLL_MS, DashboardComponent, partOfDay } from './dashboard.component';

describe('DashboardComponent', () => {
  let backend: HttpTestingController;
  let hub: FakeCrisisHub;

  function configure() {
    TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [
        provideTestTranslations(),
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideNoopAnimations(),
        { provide: API_BASE_URL, useValue: API },
        { provide: CRISIS_BASE_URL, useValue: CRISIS },
        { provide: CrisisHubService, useClass: FakeCrisisHub },
      ],
    });
  }

  async function render(language: 'ar' | 'en' = 'en', open: OpenCrisisEvent[] = [openEvent()]) {
    configure();
    const lang = TestBed.inject(LanguageService);
    await lang.use(language);
    backend = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(CrisisHubService) as unknown as FakeCrisisHub;
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush(dashboard());
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush(open);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
    // Not whenStable(): the confirmation snack bar keeps a 4 s timer pending.
    const update = async () => {
      fixture.detectChanges();
      await settle();
      await settle();
      fixture.detectChanges();
    };
    return { fixture, el, lang, overlay, update };
  }

  afterEach(() => {
    backend.verify();
    localStorage.clear();
  });

  it('greets by name with the date, and shows the day at a glance', async () => {
    const { el } = await render('en');
    expect(el.querySelector('.title')!.textContent).toContain('Sara A.');
    expect(el.querySelector('.date')!.textContent).toContain('19 October 2026');
    expect(el.querySelector('[data-testid="stat-today"]')!.textContent).toContain('4');
    expect(el.querySelector('[data-testid="stat-checkins"]')!.textContent).toContain('New check-ins this week');
    expect(el.querySelector('[data-testid="stat-capacity"] .stat-value')!.getAttribute('aria-label')).toBe('14 of 25 places used');
    const headers = Array.from(el.querySelectorAll('[data-testid="pending-table"] th')).map((th) => th.textContent!.trim());
    expect(headers.slice(0, 5)).toEqual(['Patient', 'Risk level', 'PHQ-9', 'GAD-7', 'First session']);
    expect(el.querySelectorAll('[data-testid="pending-table"] tr.mat-mdc-row').length).toBe(2);
    expect(el.querySelector('[data-testid="risk-tag"]')!.textContent).toContain('2 · elevated');
    expect(el.querySelectorAll('[data-testid="today-session"]').length).toBe(2);
    expect(hub.users).toBe(1);
  });

  it('renders each high-risk alert with its server-localized title and body', async () => {
    const { el } = await render('en');
    const alerts = el.querySelectorAll('[data-testid="crisis-alert"]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('High-risk alert for your patient P-3HX9-14QA');
    expect(alerts[0].textContent).toContain('triggered rule CR-4');
    expect(alerts[0].querySelector('[data-testid="acknowledge"]')!.textContent).toContain('Acknowledge alert');
  });

  it('shows the server problem when someone else acknowledged first (409)', async () => {
    const { el, update } = await render('en');
    (el.querySelector('[data-testid="acknowledge"]') as HTMLButtonElement).click();
    const ack = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`);
    expect(ack.request.method).toBe('POST');
    ack.flush({ title: 'Already acknowledged', detail: 'Dr Fahad R. took this case.', status: 409 }, { status: 409, statusText: 'Conflict' });
    await update();
    const problem = el.querySelector('[data-testid="crisis-alert"] [data-testid="problem"]')!;
    expect(problem.getAttribute('role')).toBe('alert');
    expect(problem.textContent).toContain('Already acknowledged');
    expect(problem.textContent).toContain('Dr Fahad R. took this case.');
  });

  it('acknowledges an alert, and links to that event on the console', async () => {
    const { el, update } = await render('en');
    expect(el.querySelector('[data-testid="open-console"]')!.getAttribute('href')).toBe('/crisis/ev-1');
    (el.querySelector('[data-testid="acknowledge"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`).flush(openEvent({ status: 'Acknowledged', kind: 'acknowledged' }));
    await update();
    expect(el.querySelector('[data-testid="acknowledge"]')).toBeNull();
    expect(el.querySelector('[data-testid="crisis-alert"]')!.textContent).toContain('Acknowledged');
  });

  it('refers an unacknowledged alert to the on-call with a note, without taking it', async () => {
    const { el, overlay, update } = await render('en');
    (el.querySelector('[data-testid="refer"]') as HTMLButtonElement).click();
    await update();
    const dialog = () => overlay().querySelector('mat-dialog-container') as HTMLElement;
    expect(dialog().textContent).toContain('Refer to the on-call');
    (dialog().querySelector('[data-testid="note-submit"]') as HTMLButtonElement).click();
    await update();
    backend.expectNone(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    const text = dialog().querySelector('[data-testid="note-text"]') as HTMLTextAreaElement;
    text.value = 'She answers the landline in the evening';
    text.dispatchEvent(new Event('input'));
    (dialog().querySelector('[data-testid="note-submit"]') as HTMLButtonElement).click();
    await update();
    const refer = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(refer.request.body).toEqual({ kind: 'refer', text: 'She answers the landline in the evening' });
    refer.flush(eventDetail({ status: 'Open' }));
    await update();
    expect(el.querySelector('[data-testid="acknowledge"]')).not.toBeNull();
  });

  it('keeps showing alerts, including live pushes, when the rest of the dashboard fails to load', fakeAsync(() => {
    configure();
    void TestBed.inject(LanguageService).use('en');
    flushMicrotasks();
    backend = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(CrisisHubService) as unknown as FakeCrisisHub;
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush({ title: 'Server error', status: 500 }, { status: 500, statusText: 'Error' });
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([openEvent()]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="problem"]')!.textContent).toContain('Server error');
    expect(el.querySelectorAll('[data-testid="crisis-alert"]').length).toBe(1);

    hub.alertsSubject.next(openEvent({ eventId: 'ev-2', title: 'New alert for P-6WQ1-33BC' }));
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="crisis-alert"]').length).toBe(2);
    fixture.destroy();
    flush();
  }));

  it('shows a reconnecting strip and polls open alerts every 5 seconds while the live connection is down', fakeAsync(() => {
    configure();
    void TestBed.inject(LanguageService).use('en');
    flushMicrotasks();
    backend = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(CrisisHubService) as unknown as FakeCrisisHub;
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush(dashboard({ alerts: [] } as Partial<TherapistDashboard>));
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([]);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('[data-testid="reconnecting"]')).toBeNull();

    hub.status.set('reconnecting');
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="reconnecting"]')!.textContent).toContain('checking for alerts every 5 seconds');
    tick(ALERT_POLL_MS);
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([openEvent()]);
    fixture.detectChanges();
    expect(el.querySelectorAll('[data-testid="crisis-alert"]').length).toBe(1);
    tick(ALERT_POLL_MS);
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([openEvent()]);

    hub.status.set('connected');
    fixture.detectChanges();
    tick(ALERT_POLL_MS * 2);
    backend.expectNone(`${CRISIS}/v1/crisis/events/open`);
    expect(el.querySelector('[data-testid="reconnecting"]')).toBeNull();
    fixture.destroy();
    discardPeriodicTasks();
    flush();
  }));

  it('adds, updates and removes alerts pushed live by the hub', async () => {
    const { el, update } = await render('en');
    hub.alertsSubject.next(openEvent({ eventId: 'ev-2', title: 'New alert for P-6WQ1-33BC' }));
    await update();
    expect(el.querySelectorAll('[data-testid="crisis-alert"]').length).toBe(2);
    hub.alertsSubject.next(openEvent({ eventId: 'ev-1', kind: 'closed', status: 'Closed' }));
    await update();
    const alerts = el.querySelectorAll('[data-testid="crisis-alert"]');
    expect(alerts.length).toBe(1);
    expect(alerts[0].textContent).toContain('P-6WQ1-33BC');
  });

  it('posts the session outcome chosen from the menu', async () => {
    const { el, overlay, update } = await render('en');
    (el.querySelector('[data-testid="record-outcome"]') as HTMLButtonElement).click();
    await update();
    const item = overlay().querySelector('[data-testid="outcome-no_show"]') as HTMLButtonElement;
    expect(item.textContent).toContain('No-show');
    item.click();
    const post = backend.expectOne(`${API}/v1/staff/appointments/ap-1/outcome`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual({ outcome: 'no_show' });
    post.flush(null, { status: 204, statusText: 'No Content' });
    await update();
    expect(el.querySelector('[data-testid="record-outcome"]')).toBeNull();
    expect(el.querySelectorAll('[data-testid="outcome-recorded"]').length).toBe(2);
  });

  it('links «review file» and each session «file» to the clinical file of the patient (S02)', async () => {
    const { el } = await render('en');
    expect(el.querySelector('[data-testid="review-file"]')!.getAttribute('href')).toBe('/patients/P-7Q4K-82XM');
    expect(el.querySelectorAll('[data-testid="open-file"]')[1].getAttribute('href')).toBe('/patients/P-3HX9-14QA');
  });

  it('reloads in the new language after a switch so server-localized text changes', async () => {
    const { lang, fixture, el } = await render('ar');
    await lang.use('en');
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush(dashboard({ alerts: [] }));
    backend.expectOne(`${CRISIS}/v1/crisis/events/open`).flush([openEvent({ title: 'English title' })]);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="crisis-alert"]')!.textContent).toContain('English title');
  });
});

describe('partOfDay', () => {
  it('greets by time of day', () => {
    expect(partOfDay(new Date(2026, 9, 19, 8))).toBe('morning');
    expect(partOfDay(new Date(2026, 9, 19, 13))).toBe('afternoon');
    expect(partOfDay(new Date(2026, 9, 19, 20))).toBe('evening');
  });
});
