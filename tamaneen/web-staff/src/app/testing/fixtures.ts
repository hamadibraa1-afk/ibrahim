import type { CrisisEventDetail, OpenCrisisEvent, TherapistDashboard } from '../core/api/types';

export const API = 'https://api.tamaneen.test';
export const CRISIS = 'https://crisis.tamaneen.test';

export function openEvent(overrides: Partial<OpenCrisisEvent> = {}): OpenCrisisEvent {
  return {
    eventId: 'ev-1',
    number: 'CE-1042',
    kind: 'opened',
    pseudonymId: 'P-3HX9-14QA',
    level: 3,
    levelLabel: 'High',
    title: 'High-risk alert for your patient P-3HX9-14QA',
    body: 'A homework note triggered rule CR-4 6 minutes ago. The on-call was notified and has not acknowledged yet.',
    patientLanguage: 'ar',
    patientLanguageName: 'Arabic',
    escalationTier: 1,
    escalationTierLabel: 'On call',
    status: 'Open',
    openedAtUtc: new Date(Date.now() - 6 * 60_000).toISOString(),
    ackDeadlineUtc: new Date(Date.now() + 90_000).toISOString(),
    categories: ['self_harm'],
    dir: 'ltr',
    source: 'intake_chat',
    sourceLabel: 'Intake chat',
    acknowledgedBy: null,
    ...overrides,
  };
}

export function eventDetail(overrides: Partial<CrisisEventDetail> = {}): CrisisEventDetail {
  return {
    ...openEvent(),
    ruleIds: ['CR-1'],
    rulePackVersion: 'en-1.4',
    contactDeadlineUtc: null,
    hasContactPhone: true,
    hasTrustedContact: true,
    locationConsent: true,
    maskedPhone: '••• ••• ••21',
    timeline: [{ at: new Date().toISOString(), kind: 'opened', text: 'Event opened automatically and the chat was paused', actor: null }],
    canClose: true,
    canAct: true,
    isOwner: false,
    pendingHandover: null,
    pendingClose: null,
    canCoSign: false,
    emergencyNumber: '997',
    emergencyScript: '1) Call emergency services on 997 and give the location shown.',
    recentCallAttempts: 0,
    callAdvice: null,
    reviewDueAtUtc: null,
    ...overrides,
  };
}

export function dashboard(overrides: Partial<TherapistDashboard> = {}): TherapistDashboard {
  return {
    displayName: 'Sara A.',
    date: '2026-10-19',
    alerts: [openEvent()],
    stats: { todaySessions: 4, pendingProfiles: 2, newCheckins: 3, capacityUsed: 14, capacityMax: 25, myPatients: 14 },
    pendingProfiles: [
      { pseudonymId: 'P-7Q4K-82XM', riskLevel: 2, phq9: 16, gad7: 12, firstSessionAt: '2026-10-21T13:00:00Z' },
      { pseudonymId: 'P-2MZR-55TD', riskLevel: 1, phq9: 9, gad7: 7, firstSessionAt: null },
    ],
    todaySessions: [
      { appointmentId: 'ap-1', startsAt: '2026-10-19T08:00:00Z', pseudonymId: 'P-9KD2-07LM', sequenceNumber: 3, riskLevel: null, outcomeRecorded: false, hasNewCheckins: false },
      { appointmentId: 'ap-2', startsAt: '2026-10-19T12:00:00Z', pseudonymId: 'P-3HX9-14QA', sequenceNumber: 2, riskLevel: 3, outcomeRecorded: true, hasNewCheckins: true },
    ],
    ...overrides,
  };
}

/** Lets pending promise continuations (e.g. the request after a resolved one) run. */
export function settle(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

export function problem(status: number, title: string, extra: Record<string, unknown> = {}) {
  return { body: { title, status, ...extra }, options: { status, statusText: 'Error' } };
}
