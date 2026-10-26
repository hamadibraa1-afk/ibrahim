import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { Alert, Alerts } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { API, CRISIS } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { AlertsComponent, outcomeKey } from './alerts.component';

const open: Alert = {
  eventId: 'e1', number: 'CE-1001', pseudonymId: 'P-3HX9-14QA', level: 3, status: 'Open', source: 'homework', ruleIds: ['CR-4'],
  openedAt: '2026-10-25T10:00:00Z', ackDeadline: '2026-10-25T10:02:00Z', acknowledgedBy: null, acknowledgedAt: null, closedAt: null, closeOutcome: null,
};
const closed: Alert = { ...open, eventId: 'e2', number: 'CE-1000', pseudonymId: 'P-5RT3-20MN', status: 'Closed', closedAt: '2026-10-03T10:00:00Z', closeOutcome: 'false_alarm' };
const alerts: Alerts = { items: [open, closed], openCount: 1, handledCount: 1 };

describe('AlertsComponent (S20)', () => {
  let backend: HttpTestingController;

  async function render(language: 'ar' | 'en' = 'en') {
    TestBed.configureTestingModule({
      imports: [AlertsComponent],
      providers: [provideTestTranslations(), provideHttpClient(), provideHttpClientTesting(), provideRouter([]), provideNoopAnimations(), { provide: API_BASE_URL, useValue: API }, { provide: CRISIS_BASE_URL, useValue: CRISIS }],
    });
    await TestBed.inject(LanguageService).use(language);
    backend = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(AlertsComponent);
    fixture.detectChanges();
    backend.expectOne(`${API}/v1/staff/alerts?status=all`).flush(alerts);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement };
  }

  afterEach(() => backend.verify());

  it('shows the open alert with its rule, source and acknowledgement state, and links to the console', async () => {
    const { el } = await render();
    const card = el.querySelector('[data-testid="open-alert"]')!;
    expect(card.textContent).toContain('P-3HX9-14QA');
    expect(card.textContent).toContain('rule CR-4');
    expect(card.textContent).toContain('Homework note');
    expect(card.textContent).toContain('not acknowledged yet');
    expect(card.querySelector('[data-testid="open-event"]')!.getAttribute('href')).toBe('/crisis/e1');
    expect(el.querySelector('[data-testid="history-alert"]')).toBeNull();
  });

  it('acknowledges through the crisis worker and refreshes', async () => {
    const { el } = await render();
    (el.querySelector('[data-testid="ack"]') as HTMLButtonElement).click();
    backend.expectOne(`${CRISIS}/v1/crisis/events/e1/acknowledge`).flush({});
    backend.expectOne(`${API}/v1/staff/alerts?status=all`).flush({ ...alerts, items: [{ ...open, status: 'Acknowledged', acknowledgedBy: 'Sara M.' }, closed] });
  });

  it('shows the 30-day history in Arabic with the final state', async () => {
    const { fixture, el } = await render('ar');
    (el.querySelector('[data-testid="status-handled"] button') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="open-alert"]')).toBeNull();
    expect(el.querySelector('[data-testid="history-alert"]')!.textContent).toContain('إنذار خاطئ');
    expect(outcomeKey(open)).toBe('alerts.outcome.closed');
  });
});
