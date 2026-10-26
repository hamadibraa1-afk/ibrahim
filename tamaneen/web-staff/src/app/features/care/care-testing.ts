import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { type Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, CRISIS_BASE_URL } from '../../core/api';
import type { PatientOverview } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import type { StaffRole } from '../../core/session/staff-role';
import { API, CRISIS, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';

export const PID = 'P-4TT8-90AA';
export const PATIENT_URL = `${API}/v1/staff/patients/${PID}`;

/** Renders a care screen with the real translations, a fake backend and the given role. */
export async function renderCare<T>(component: Type<T>, inputs: Record<string, unknown>, options: { language?: 'ar' | 'en'; role?: StaffRole } = {}) {
  TestBed.configureTestingModule({
    imports: [component],
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
  await TestBed.inject(LanguageService).use(options.language ?? 'en');
  TestBed.inject(SessionService).setRoles([options.role ?? 'therapist']);
  const backend = TestBed.inject(HttpTestingController);
  const fixture = TestBed.createComponent(component);
  for (const [key, value] of Object.entries(inputs)) {
    fixture.componentRef.setInput(key, value);
  }
  fixture.detectChanges();
  const el = fixture.nativeElement as HTMLElement;
  const update = async () => {
    fixture.detectChanges();
    await settle();
    await settle();
    fixture.detectChanges();
  };
  const overlay = () => document.querySelector('.cdk-overlay-container') as HTMLElement;
  const q = (id: string, root: ParentNode = el) => root.querySelector(`[data-testid="${id}"]`) as HTMLElement | null;
  return { fixture, el, backend, update, overlay, q };
}

/** Types into an input or textarea the way a user does. */
export function type(element: HTMLElement | null, value: string): void {
  const input = element as HTMLInputElement | HTMLTextAreaElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

export function overview(overrides: Partial<PatientOverview> = {}): PatientOverview {
  return {
    pseudonymId: PID,
    riskLevel: 1,
    crisisFloor: 0,
    careStartedAt: new Date(Date.now() - 35 * 86_400_000).toISOString(),
    inCare: true,
    closureStatus: null,
    fileApproved: true,
    phq9AtIntake: 14,
    gad7AtIntake: 9,
    nextAppointment: { id: 'ap-5', startsAt: '2030-10-21T10:00:00Z', durationMinutes: 50, sequenceNumber: 5, status: 'confirmed', noteStatus: 'none' },
    pastAppointments: [{ id: 'ap-4', startsAt: '2026-10-14T10:00:00Z', durationMinutes: 50, sequenceNumber: 4, status: 'completed', noteStatus: 'signed' }],
    mood: [
      { at: '2026-10-18T08:00:00Z', score: 5 },
      { at: '2026-10-19T08:00:00Z', score: 7 },
    ],
    moodAverage7Days: 6.4,
    plan: { version: 2, createdAt: '2026-10-05T08:00:00Z', reviewOn: '2026-11-30', goals: [{ text: 'Fall asleep within 30 minutes', status: 'improving' }, { text: 'Weekly exercise', status: 'not_started' }] },
    homework: [{ id: 'hw-1', title: 'Breathing before bed', frequency: 'daily', doneThisWeek: 6, weeklyTarget: 7, averageFeeling: 6.4 }],
    safetyPlan: { exists: true, version: 2, updatedAt: '2026-10-18T08:00:00Z', updatedByRole: 'therapist' },
    alerts: [],
    canEdit: true,
    ...overrides,
  };
}
