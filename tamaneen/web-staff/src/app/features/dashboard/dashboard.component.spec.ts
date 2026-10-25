import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { FakeCrisisHub } from '../../testing/fake-crisis-hub';
import { API, CRISIS, dashboard, eventDetail, openEvent, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { DashboardComponent, partOfDay } from './dashboard.component';

describe('DashboardComponent', () => {
  let backend: HttpTestingController;
  let hub: FakeCrisisHub;

  async function render(language: 'ar' | 'en' = 'en') {
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
    const lang = TestBed.inject(LanguageService);
    await lang.use(language);
    backend = TestBed.inject(HttpTestingController);
    hub = TestBed.inject(CrisisHubService) as unknown as FakeCrisisHub;
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/dashboard`).flush(dashboard());
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

  it('acknowledges and refers alerts to the on-call team', async () => {
    const { el, update } = await render('en');
    (el.querySelector('[data-testid="acknowledge"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/acknowledge`).flush(openEvent({ status: 'Acknowledged', kind: 'acknowledged' }));
    await update();
    expect(el.querySelector('[data-testid="acknowledge"]')).toBeNull();
    expect(el.querySelector('[data-testid="crisis-alert"]')!.textContent).toContain('Acknowledged');

    (el.querySelector('[data-testid="refer"]') as HTMLButtonElement).click();
    const refer = backend.expectOne(`${CRISIS}/v1/crisis/events/ev-1/actions`);
    expect(refer.request.body).toEqual({ kind: 'handover' });
    refer.flush(eventDetail({ status: 'Open' }));
    await update();
    expect(el.querySelector('[data-testid="acknowledge"]')).not.toBeNull();
  });

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
    const again = backend.expectOne(`${API}/v1/staff/dashboard`);
    again.flush(dashboard({ alerts: [openEvent({ title: 'English title' })] }));
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
