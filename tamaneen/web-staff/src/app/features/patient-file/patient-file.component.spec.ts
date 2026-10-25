import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { Router, provideRouter } from '@angular/router';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { PatientFile } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { API, CRISIS, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { PatientFileComponent } from './patient-file.component';

const ID = 'P-7Q4K-82XM';
const FILE_URL = `${API}/v1/staff/patients/${ID}/file`;

export function patientFile(overrides: Partial<PatientFile> = {}): PatientFile {
  return {
    pseudonymId: ID,
    riskLevel: 2,
    crisisFloor: 0,
    firstSessionAt: '2026-10-21T13:00:00Z',
    path: 'intake',
    version: 'v1',
    generatedByAi: true,
    statements: [
      { key: 'concern', text: 'Work stress for about three months, trouble falling asleep.', editedByPatient: false, editedByTherapist: false, hypothesis: false, sourceCount: 2 },
      { key: 'goals', text: 'Calm down before sleep and get back to sport.', editedByPatient: true, editedByTherapist: false, hypothesis: false, sourceCount: 1 },
      { key: 'hypotheses', text: 'Symptoms consistent with stress-related anxiety.', editedByPatient: false, editedByTherapist: false, hypothesis: true, sourceCount: 1 },
    ],
    transcript: [
      { id: 'm1', turn: 1, role: 'assistant', text: 'How have you been feeling?', at: '2026-10-18T10:00:00Z' },
      { id: 'm2', turn: 2, role: 'patient', text: 'I feel a lot of pressure and do not sleep well.', at: '2026-10-18T10:01:00Z' },
      { id: 'm3', turn: 3, role: 'assistant', text: 'Since when?', at: '2026-10-18T10:02:00Z' },
      { id: 'm4', turn: 4, role: 'patient', text: 'About three months, since my manager changed.', at: '2026-10-18T10:03:00Z' },
    ],
    transcriptRemoved: false,
    phq9: { instrument: 'PHQ-9', total: 16, severity: 'moderately_severe', item9: 0, version: 'q-2026.10', items: [{ item: 9, text: 'Thoughts that you would be better off dead', value: 0 }] },
    gad7: { instrument: 'GAD-7', total: 12, severity: 'moderate', item9: null, version: 'q-2026.10', items: [{ item: 1, text: 'Feeling nervous', value: 2 }] },
    cssrs: null,
    corrections: [{ key: 'goals', aiText: 'Learn ways to calm down before bed.', patientText: 'Calm down before sleep and get back to sport.' }],
    addenda: [],
    approval: { approved: false, approvedBy: null, approvedAt: null },
    feedback: { rating: null, comment: null },
    canEdit: true,
    submittedAt: '2026-10-18T10:05:00Z',
    ...overrides,
  };
}

describe('PatientFileComponent (S02)', () => {
  let backend: HttpTestingController;

  async function render(file: PatientFile | null = patientFile(), role: 'therapist' | 'supervisor' = 'therapist') {
    TestBed.configureTestingModule({
      imports: [PatientFileComponent],
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
    const router = TestBed.inject(Router);
    const navigate = spyOn(router, 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(PatientFileComponent);
    fixture.componentRef.setInput('id', ID);
    fixture.detectChanges();
    if (file) {
      backend.expectOne(FILE_URL).flush(file);
    } else {
      backend.expectOne(FILE_URL).flush({ title: 'Not found', status: 404 }, { status: 404, statusText: 'Not Found' });
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
    return { fixture, el, overlay, update, click, navigate };
  }

  afterEach(() => {
    backend.verify();
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  it('shows the header, the score cards and the AI statements with their sources and flags', async () => {
    const { el } = await render();
    expect(el.querySelector('[data-testid="file-pseudonym"]')!.textContent).toContain(ID);
    expect(el.querySelector('[data-testid="risk-tag"]')!.textContent).toContain('2 · elevated');
    expect(el.querySelector('[data-testid="score-PHQ-9"]')!.textContent).toContain('16');
    expect(el.querySelector('[data-testid="score-PHQ-9"]')!.textContent).toContain('moderately severe');
    expect(el.querySelector('[data-testid="item9"]')!.textContent).toContain('PHQ-9 item 9 = 0');
    expect(el.querySelector('[data-testid="statement-concern"] [data-testid="sources"]')!.textContent).toContain('Source: 2 messages');
    expect(el.querySelector('[data-testid="statement-goals"] [data-testid="corrected"]')!.textContent).toContain('Corrected by the patient');
    const hypotheses = el.querySelector('[data-testid="statement-hypotheses"]')!;
    expect(hypotheses.textContent).toContain('not a diagnosis');
    expect(hypotheses.querySelector('[data-testid="edit"]')).toBeNull();
    expect(el.querySelectorAll('[data-testid="turn"]').length).toBe(4);
    expect(el.textContent).toContain('Original messages are deleted 90 days after approval');
  });

  it('highlights the source messages of a statement (audited evidence call)', async () => {
    const { el, click, update } = await render();
    click('[data-testid="statement-concern"] [data-testid="sources"]');
    backend.expectOne(`${API}/v1/staff/patients/${ID}/evidence/concern`).flush({ statementKey: 'concern', messageIds: ['m2', 'm4'], turns: [2, 4], removed: false });
    await update();
    const sources = el.querySelectorAll('[data-testid="source-turn"]');
    expect(sources.length).toBe(2);
    expect(sources[0].textContent!.replace(/\s+/g, ' ')).toContain('Patient · turn 2 · source');
    expect(el.querySelector('[data-testid="statement-concern"]')!.classList).toContain('selected');
  });

  it('shows «source deleted» after the 90-day purge', async () => {
    const { el, click, update } = await render(patientFile({ transcript: [], transcriptRemoved: true }));
    click('[data-testid="statement-concern"] [data-testid="sources"]');
    backend.expectOne(`${API}/v1/staff/patients/${ID}/evidence/concern`).flush({ statementKey: 'concern', messageIds: [], turns: [], removed: true });
    await update();
    expect(el.querySelector('[data-testid="source-removed"]')!.textContent).toContain('The source was deleted.');
  });

  it('keeps edits local and sends them with the approval and the version read (If-Match)', async () => {
    const { el, click, update } = await render();
    click('[data-testid="statement-concern"] [data-testid="edit"]');
    await update();
    const area = el.querySelector('[data-testid="edit-text"]') as HTMLTextAreaElement;
    area.value = 'Work stress, initial insomnia.';
    area.dispatchEvent(new Event('input'));
    click('[data-testid="edit-save"]');
    await update();
    expect(el.querySelector('[data-testid="statement-concern"]')!.textContent).toContain('Work stress, initial insomnia.');
    expect(el.querySelector('[data-testid="statement-concern"] [data-testid="edited"]')).not.toBeNull();

    click('[data-testid="approve"]');
    const req = backend.expectOne(`${API}/v1/staff/patients/${ID}/file/approve`);
    expect(req.request.headers.get('If-Match')).toBe('"v1"');
    expect(req.request.body).toEqual({ edits: { concern: 'Work stress, initial insomnia.' } });
    req.flush(patientFile({ version: 'v2', canEdit: false, approval: { approved: true, approvedBy: 'Sara M.', approvedAt: '2026-10-19T09:00:00Z' } }));
    await update();
    expect(el.querySelector('[data-testid="approved"]')!.textContent).toContain('File approved by Sara M.');
    expect(el.querySelector('[data-testid="approve"]')).toBeNull();
    expect(el.querySelector('[data-testid="dismiss"]')).toBeNull();
  });

  it('on 412 shows the latest version and asks to approve again', async () => {
    const { el, click, update } = await render();
    click('[data-testid="approve"]');
    backend.expectOne(`${API}/v1/staff/patients/${ID}/file/approve`).flush({ title: 'Changed', status: 412 }, { status: 412, statusText: 'Precondition Failed' });
    backend.expectOne(FILE_URL).flush(patientFile({ version: 'v3', addenda: ['New note from the patient'] }));
    await update();
    expect(el.querySelector('[data-testid="conflict"]')!.textContent).toContain('The file changed since you opened it');
    click('[data-testid="approve"]');
    expect(backend.expectOne(`${API}/v1/staff/patients/${ID}/file/approve`).request.headers.get('If-Match')).toBe('"v3"');
  });

  it('dismisses a statement only with a reason', async () => {
    const { el, click, overlay, update } = await render();
    click('[data-testid="statement-hypotheses"] [data-testid="dismiss"]');
    await update();
    click('[data-testid="reason-submit"]', overlay());
    await update();
    backend.expectNone(`${API}/v1/staff/patients/${ID}/file/dismiss-item`);
    const reason = overlay().querySelector('[data-testid="reason-text"]') as HTMLTextAreaElement;
    reason.value = 'Too early to say';
    reason.dispatchEvent(new Event('input'));
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${API}/v1/staff/patients/${ID}/file/dismiss-item`);
    expect(req.request.body).toEqual({ statementKey: 'hypotheses', reason: 'Too early to say' });
    req.flush(patientFile({ version: 'v2', statements: patientFile().statements.filter((s) => s.key !== 'hypotheses') }));
    await update();
    expect(el.querySelector('[data-testid="statement-hypotheses"]')).toBeNull();
  });

  it('rates the file from 1 to 5', async () => {
    const { el, click, update } = await render();
    click('[data-testid="rate-4"]');
    const req = backend.expectOne(`${API}/v1/staff/patients/${ID}/file-feedback`);
    expect(req.request.body).toEqual({ rating: 4, comment: undefined });
    req.flush(null, { status: 204, statusText: 'No Content' });
    await update();
    expect(el.querySelector('[data-testid="rate-4"]')!.getAttribute('aria-pressed')).toBe('true');
  });

  it('raises the risk only to a higher level, with a reason', async () => {
    const { click, overlay, update } = await render();
    click('[data-testid="raise-risk"]');
    await update();
    const levels = Array.from(overlay().querySelectorAll('mat-radio-button')).map((r) => r.getAttribute('data-testid'));
    expect(levels).toEqual(['level-3', 'level-4']);
    (overlay().querySelector('[data-testid="level-3"] input') as HTMLInputElement).click();
    const reason = overlay().querySelector('[data-testid="reason-text"]') as HTMLTextAreaElement;
    reason.value = 'Sleep is getting worse';
    reason.dispatchEvent(new Event('input'));
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${API}/v1/staff/patients/${ID}/risk-flag`);
    expect(req.request.body).toEqual({ riskLevel: 3, reason: 'Sleep is getting worse' });
    req.flush({ riskLevel: 3 });
    await update();
    backend.expectOne(FILE_URL).flush(patientFile({ riskLevel: 3 }));
  });

  it('raises a manual crisis alert with a note and opens the event on the console', async () => {
    const { click, overlay, update, navigate } = await render();
    click('[data-testid="manual-crisis"]');
    await update();
    (overlay().querySelector('[data-testid="level-4"] input') as HTMLInputElement).click();
    const note = overlay().querySelector('[data-testid="reason-text"]') as HTMLTextAreaElement;
    note.value = 'Talked about a plan in session';
    note.dispatchEvent(new Event('input'));
    click('[data-testid="reason-submit"]', overlay());
    const req = backend.expectOne(`${API}/v1/staff/patients/${ID}/crisis`);
    expect(req.request.body).toEqual({ level: 4, note: 'Talked about a plan in session' });
    req.flush({ crisisEventId: 'ev-9', level: 4 });
    await update();
    expect(navigate).toHaveBeenCalledWith(['/crisis', 'ev-9']);
  });

  it('shows item answers, C-SSRS and the patient corrections on their tabs', async () => {
    const { el, click, update } = await render(patientFile({ cssrs: { positive: true, level: 3, version: 'cssrs-1', completedAt: '2026-10-18T09:00:00Z', items: [{ index: 1, text: 'Wished you were dead?', answer: true }] } }));
    click('[data-testid="tab-scores"]');
    await update();
    expect(el.querySelector('[data-testid="items-PHQ-9"]')!.textContent).toContain('Instrument version q-2026.10');
    click('[data-testid="tab-cssrs"]');
    await update();
    expect(el.querySelector('[data-testid="cssrs-panel"]')!.textContent).toContain('Positive');
    expect(el.querySelector('[data-testid="cssrs-panel"]')!.textContent).toContain('Yes');
    click('[data-testid="tab-corrections"]');
    await update();
    expect(el.querySelector('[data-testid="corrections-panel"]')!.textContent).toContain('Learn ways to calm down before bed.');
  });

  it('is read only for a supervisor, and says «not found» for a patient not assigned', async () => {
    const { el } = await render(patientFile({ canEdit: false }), 'supervisor');
    expect(el.querySelector('[data-testid="approve"]')).toBeNull();
    expect(el.querySelector('[data-testid="rating"]')).toBeNull();
    expect(el.querySelector('[data-testid="raise-risk"]')).not.toBeNull();
    TestBed.resetTestingModule();
    const missing = await render(null);
    expect(missing.el.querySelector('[data-testid="not-found"]')!.textContent).toContain('Not found');
  });
});
