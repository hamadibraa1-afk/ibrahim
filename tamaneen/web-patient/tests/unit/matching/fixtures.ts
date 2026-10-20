import type { Appointment, AppointmentDetail, MatchSuggestion, TherapistSummary } from '@/lib/api/types';

export const NOW = new Date('2026-10-19T09:00:00Z'); // Monday 19 October, 12:00 in Riyadh

export function therapist(overrides: Partial<TherapistSummary> = {}): TherapistSummary {
  return {
    id: 't-sara',
    displayName: 'أ. سارة م.',
    initials: 'س م',
    title: 'أخصائية نفسية إكلينيكية',
    specialties: ['anxiety', 'sleep', 'cbt'],
    languages: ['ar', 'en'],
    gender: 'female',
    licenseVerified: true,
    nextAvailableAt: '2026-10-21T13:00:00Z',
    hasEvening: false,
    ...overrides,
  };
}

export const sara = therapist();
export const khaled = therapist({
  id: 't-khaled',
  displayName: 'أ. خالد ع.',
  initials: 'خ ع',
  title: 'أخصائي نفسي',
  specialties: ['anxiety', 'mindfulness'],
  gender: 'male',
  nextAvailableAt: '2026-10-20T15:00:00Z',
  hasEvening: true,
});
export const noura = therapist({ id: 't-noura', displayName: 'أ. نورة ح.', initials: 'ن ح', specialties: ['work_stress', 'adjustment'] });

export function suggestion(t: TherapistSummary, rank: number, reasons: Partial<MatchSuggestion['reasons']> = {}): MatchSuggestion {
  return {
    therapist: t,
    rank,
    best: rank === 1,
    reasons: { sharedSpecialties: [], speaksYourLanguage: false, eveningSlots: false, nextAvailableAt: null, ...reasons },
  };
}

export function appointment(overrides: Partial<Appointment> = {}): Appointment {
  return {
    id: 'a1',
    therapist: sara,
    startsAt: '2026-10-21T13:00:00Z',
    durationMinutes: 50,
    sequenceNumber: 1,
    status: 'confirmed',
    rescheduled: false,
    ...overrides,
  };
}

export function appointmentDetail(overrides: Partial<AppointmentDetail> = {}): AppointmentDetail {
  return {
    ...appointment(),
    canChange: true,
    changeDeadline: '2026-10-20T13:00:00Z',
    joinUrl: null,
    joinAvailableAt: '2026-10-21T12:50:00Z',
    ...overrides,
  };
}
