import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { TriageCaseDetail, TriageQueueItem } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { API, CRISIS, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { TriageReviewComponent, timeLeft } from './triage-review.component';

const QUEUE = `${API}/v1/staff/triage`;
const CASE = `${QUEUE}/c-1`;

function queueItem(overrides: Partial<TriageQueueItem> = {}): TriageQueueItem {
  return {
    id: 'c-1',
    pseudonymId: 'P-7Q4K-82XM',
    path: 'intake',
    contactRequested: false,
    riskLevel: 2,
    phq9: 16,
    gad7: 12,
    cssrsPositive: false,
    submittedAt: new Date(Date.now() - 80 * 60_000).toISOString(),
    dueAt: new Date(Date.now() + 160 * 60_000 + 30_000).toISOString(),
    claim: { by: null, until: null, mine: false },
    pendingRiskLevel: null,
    ...overrides,
  };
}

function caseDetail(overrides: Partial<TriageCaseDetail> = {}): TriageCaseDetail {
  return {
    id: 'c-1',
    pseudonymId: 'P-7Q4K-82XM',
    path: 'intake',
    contactRequested: false,
    status: 'pending',
    riskLevel: 2,
    ruleRiskLevel: 2,
    ruleReasons: ['phq9'],
    aiRiskLevel: null,
    crisisFloor: 0,
    phq9: 16,
    gad7: 12,
    cssrsPositive: false,
    phq9Item9: 0,
    language: 'ar',
    carePath: 'intake',
    summary: 'Work stress for three months with trouble falling asleep.',
    suggestions: [
      { id: 't-1', rank: 1, displayName: 'Sara M.', specialties: ['stress', 'sleep'], shared: ['stress'], languages: ['ar', 'en'], load: 14, capacity: 25, nextAvailableAt: '2026-10-22T13:00:00Z' },
      { id: 't-2', rank: 2, displayName: 'Khalid A.', specialties: ['anxiety'], shared: [], languages: ['ar'], load: 9, capacity: 20, nextAvailableAt: null },
    ],
    suggestionsSaved: false,
    claim: { by: 'Dr. Fahad R.', until: new Date(Date.now() + 15 * 60_000).toISOString(), mine: true },
    pendingCoSign: null,
    canCoSign: false,
    submittedAt: new Date().toISOString(),
    dueAt: new Date(Date.now() + 4 * 3600_000).toISOString(),
    serverNow: new Date().toISOString(),
    ...overrides,
  };
}

describe('TriageReviewComponent (S03)', () => {
  let backend: HttpTestingController;

  async function render(id: string | null = 'c-1', claim: 'mine' | 'other' = 'mine', role: 'supervisor' | 'lead' = 'supervisor', detail = caseDetail()) {
    TestBed.configureTestingModule({
      imports: [TriageReviewComponent],
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
    await TestBed.inject(LanguageService).use('en');
    TestBed.inject(SessionService).setRoles([role]);
    backend = TestBed.inject(HttpTestingController);
    const navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(TriageReviewComponent);
    if (id) {
      fixture.componentRef.setInput('id', id);
    }
    fixture.detectChanges();
    backend.expectOne(QUEUE).flush([queueItem(), queueItem({ id: 'c-2', pseudonymId: 'P-5CC0-18VW', riskLevel: 1, claim: { by: 'Dr. Hind S.', until: new Date(Date.now() + 600_000).toISOString(), mine: false } })]);
    if (id) {
      if (claim === 'mine') {
        backend.expectOne(`${CASE}/claim`).flush(detail);
        backend.expectOne(QUEUE).flush([queueItem()]);
      } else {
        backend.expectOne(`${CASE}/claim`).flush({ title: 'Claimed', status: 409, code: 'error.triage.claimed', claimedBy: 'Dr. Hind S.', claimedUntil: '2026-10-19T10:55:00Z' }, { status: 409, statusText: 'Conflict' });
        backend.expectOne(CASE).flush({ ...detail, claim: { by: 'Dr. Hind S.', until: '2026-10-19T10:55:00Z', mine: false } });
      }
    }
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
    const update = async () => {
      fixture.detectChanges();
      await settle();
      await settle();
      fixture.detectChanges();
    };
    const click = (selector: string, root: HTMLElement = el) => (root.querySelector(selector) as HTMLElement).click();
    const typeReason = (text: string) => {
      const area = el.querySelector('[data-testid="decision-reason"]') as HTMLTextAreaElement;
      area.value = text;
      area.dispatchEvent(new Event('input'));
    };
    return { fixture, el, overlay, update, click, typeReason, navigate };
  }

  afterEach(() => {
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  it('computes the time left on the review deadline', () => {
    const now = Date.parse('2026-10-19T08:00:00Z');
    expect(timeLeft('2026-10-19T10:40:30Z', now)).toEqual({ hours: 2, minutes: 40, overdue: false });
    expect(timeLeft('2026-10-19T07:00:00Z', now).overdue).toBeTrue();
  });

  it('lists the queue with source, time left and who holds a case', async () => {
    const { el } = await render(null);
    const items = el.querySelectorAll('[data-testid="queue-item"]');
    expect(items.length).toBe(2);
    expect(items[0].textContent).toContain('Full intake');
    expect(items[0].textContent).toContain('2 h 40 min left');
    expect(items[1].textContent).toContain('Held by Dr. Hind S.');
    expect(el.textContent).toContain('Choose a case from the queue.');
    backend.verify();
  });

  it('claims the case on opening, shows the levels, safety, summary and suggestions, and releases on leaving', async () => {
    const { el, fixture } = await render();
    expect(el.querySelector('[data-testid="claim-bar"]')!.textContent).toContain('Held by you until');
    expect(el.querySelector('[data-testid="rule-level"]')!.textContent).toContain('PHQ-9 = 16');
    expect(el.textContent).toContain('The AI cannot lower the level');
    expect(el.querySelector('[data-testid="safety"]')!.textContent).toContain('C-SSRS negative');
    expect(el.querySelectorAll('[data-testid="suggested"]').length).toBe(2);
    expect(el.querySelector('[data-testid="suggested"]')!.textContent).toContain('capacity 14/25');
    expect(el.querySelector('[data-testid="full-file"]')!.getAttribute('href')).toBe('/patients/P-7Q4K-82XM/file');
    fixture.destroy();
    backend.expectOne((r) => r.method === 'DELETE' && r.url === `${CASE}/claim`).flush(null, { status: 204, statusText: 'No Content' });
    backend.verify();
  });

  it('is read only when another supervisor holds the case', async () => {
    const { el } = await render('c-1', 'other');
    expect(el.querySelector('[data-testid="held-by"]')!.textContent).toContain('Dr. Hind S.');
    expect((el.querySelector('[data-testid="approve"]') as HTMLButtonElement).disabled).toBeTrue();
    expect((el.querySelector('[data-testid="escalate"]') as HTMLButtonElement).disabled).toBeTrue();
    backend.verify();
  });

  it('approves with the suggestions shown, without needing a reason', async () => {
    const { click, update, navigate } = await render();
    click('[data-testid="approve"]');
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'approve', reason: undefined, therapistIds: ['t-1', 't-2'] });
    req.flush({ kind: 'approve', status: 'reviewed', riskLevel: 2, coSignRequested: false, crisisEventId: null });
    await update();
    backend.expectOne(QUEUE).flush([]);
    expect(navigate).toHaveBeenCalledWith(['/triage']);
  });

  it('needs a reason to change the level, and explains the co-sign when lowering from high', async () => {
    const { el, click, update, overlay, typeReason } = await render('c-1', 'mine', 'supervisor', caseDetail({ riskLevel: 3, ruleRiskLevel: 3 }));
    click('[data-testid="override"]');
    await update();
    expect(el.querySelector('[data-testid="reason-missing"]')!.textContent).toContain('Write the reason');
    expect(document.querySelector('mat-dialog-container')).toBeNull();

    typeReason('Spoke with the patient; no current risk');
    click('[data-testid="override"]');
    await update();
    expect(overlay().textContent).toContain('applies only after the clinical lead co-signs');
    (overlay().querySelector('[data-testid="level-1"] input') as HTMLInputElement).click();
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'override_risk', riskLevel: 1, reason: 'Spoke with the patient; no current risk' });
    req.flush({ kind: 'override_risk', status: 'pending', riskLevel: 3, coSignRequested: true, crisisEventId: null });
    await update();
    backend.expectOne(CASE).flush(caseDetail({ riskLevel: 3, pendingCoSign: { riskLevel: 1, requestedBy: 'Dr. Fahad R.', requestedAt: new Date().toISOString() } }));
    backend.expectOne(QUEUE).flush([]);
    await update();
    expect(el.querySelector('[data-testid="pending-cosign"]')!.textContent).toContain('waits for the clinical lead');
    expect((el.querySelector('[data-testid="approve"]') as HTMLButtonElement).disabled).toBeTrue();
  });

  it('never offers a level below the crisis floor', async () => {
    const { click, update, overlay, typeReason } = await render('c-1', 'mine', 'supervisor', caseDetail({ riskLevel: 4, crisisFloor: 3 }));
    typeReason('Reviewed');
    click('[data-testid="override"]');
    await update();
    const levels = Array.from(overlay().querySelectorAll('mat-radio-button')).map((r) => r.getAttribute('data-testid'));
    expect(levels).toEqual(['level-3']);
  });

  it('lets the clinical lead co-sign a pending lowering with a reason', async () => {
    const detail = caseDetail({ riskLevel: 3, claim: { by: null, until: null, mine: false }, canCoSign: true, pendingCoSign: { riskLevel: 1, requestedBy: 'Dr. Fahad R.', requestedAt: new Date().toISOString() } });
    const { click, update, typeReason } = await render('c-1', 'mine', 'lead', detail);
    typeReason('Agree after reading the file');
    click('[data-testid="cosign-approve"]');
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'cosign', approve: true, reason: 'Agree after reading the file' });
    req.flush({ kind: 'cosign', status: 'pending', riskLevel: 1, coSignRequested: false, crisisEventId: null });
    await update();
    backend.expectOne(CASE).flush(caseDetail({ riskLevel: 1 }));
    backend.expectOne(QUEUE).flush([]);
  });

  it('escalates to a crisis and opens the event on the console', async () => {
    const { click, update, overlay, typeReason, navigate } = await render();
    typeReason('Needs the crisis team now');
    click('[data-testid="escalate"]');
    await update();
    (overlay().querySelector('[data-testid="level-4"] input') as HTMLInputElement).click();
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'escalate', riskLevel: 4, reason: 'Needs the crisis team now' });
    req.flush({ kind: 'escalate', status: 'escalated', riskLevel: 4, coSignRequested: false, crisisEventId: 'ev-7' });
    await update();
    expect(navigate).toHaveBeenCalledWith(['/crisis', 'ev-7']);
  });

  it('closes with the chosen referrals and keeps the reason when saving fails', async () => {
    const { el, click, update, overlay, typeReason } = await render();
    typeReason('Better served by primary care');
    click('[data-testid="close"]');
    await update();
    click('[data-testid="reason-submit"]', overlay());
    await update();
    expect(overlay().textContent).toContain('Choose at least one');
    (overlay().querySelector('[data-testid="choice-primary_care"] input') as HTMLInputElement).click();
    await update();
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'close', referrals: ['primary_care'], reason: 'Better served by primary care' });
    req.flush({ title: 'Your claim ended', status: 409, code: 'error.triage.claim_required' }, { status: 409, statusText: 'Conflict' });
    await update();
    expect(overlay().textContent).toContain('Your claim ended');
    expect((el.querySelector('[data-testid="decision-reason"]') as HTMLTextAreaElement).value).toBe('Better served by primary care');
  });

  it('replaces the suggestions from the approved therapists with room (up to three)', async () => {
    const { click, update, overlay, typeReason } = await render();
    typeReason('Evening sessions');
    click('[data-testid="change-suggestions"]');
    backend.expectOne(`${CASE}/therapists`).flush([
      { id: 't-3', rank: 1, displayName: 'Noura H.', specialties: ['stress'], shared: [], languages: ['ar'], load: 18, capacity: 25, nextAvailableAt: null },
    ]);
    await update();
    expect(overlay().textContent).toContain('Noura H. · Arabic · capacity 18/25');
    (overlay().querySelector('[data-testid="choice-t-3"] input') as HTMLInputElement).click();
    await update();
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${CASE}/decisions`);
    expect(req.request.body).toEqual({ kind: 'reassign', therapistIds: ['t-3'], reason: 'Evening sessions' });
    req.flush({ kind: 'reassign', status: 'pending', riskLevel: 2, coSignRequested: false, crisisEventId: null });
    await update();
    backend.expectOne(CASE).flush(caseDetail());
    backend.expectOne(QUEUE).flush([]);
  });

  it('warns two minutes before the claim ends, then asks to claim again', async () => {
    const { el, fixture } = await render('c-1', 'mine', 'supervisor', caseDetail({ claim: { by: 'me', until: new Date(Date.now() + 90_000).toISOString(), mine: true } }));
    expect(el.querySelector('[data-testid="claim-bar"]')!.textContent).toContain('ends in');
    const component = fixture.componentInstance as unknown as { now: { set(v: number): void } };
    component.now.set(Date.now() + 120_000);
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="claim-expired"]')).not.toBeNull();
    expect((el.querySelector('[data-testid="approve"]') as HTMLButtonElement).disabled).toBeTrue();
  });
});
