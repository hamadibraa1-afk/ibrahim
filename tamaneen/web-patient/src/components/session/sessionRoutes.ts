import type { Appointment } from '@/lib/api/types';

type RouteInput = Pick<Appointment, 'id' | 'status' | 'cancelledBy' | 'missed' | 'startsAt' | 'durationMinutes'>;

const base = (id: string) => `/appointments/${encodeURIComponent(id)}`;

/**
 * Where a session that did not take place belongs (P41, P54, P43):
 * - the therapist cancelled → P67 `/cancelled`;
 * - the therapist recorded a no-show → P63 `/missed`;
 * - any other cancellation → P28.
 * Returns null while the session is still confirmed or completed.
 */
export function notHeldRoute(a: Pick<Appointment, 'id' | 'status' | 'cancelledBy' | 'missed'>): string | null {
  if (a.status === 'cancelled_by_therapist' && a.cancelledBy !== 'care_ended') return `${base(a.id)}/cancelled`;
  if (a.missed || a.status === 'no_show') return `${base(a.id)}/missed`;
  if (a.status === 'cancelled_by_patient' || a.status === 'cancelled_by_therapist') return base(a.id);
  return null;
}

/**
 * Whether P43 «أنهيت جلستك» fits: the therapist recorded the session as completed, or it is confirmed, has started
 * and the therapist was in the room (the outcome is not recorded yet).
 */
export function sessionHeld(a: Pick<Appointment, 'status' | 'startsAt'>, therapistJoined: boolean, now: number): boolean {
  if (a.status === 'completed') return true;
  return a.status === 'confirmed' && therapistJoined && now >= Date.parse(a.startsAt);
}

/**
 * P43 guard: null when the summary may show, otherwise where to send the patient instead. A confirmed session the
 * therapist never joined goes back to the lobby while its time lasts and to P28 after it.
 */
export function summaryRedirect(a: RouteInput, therapistJoined: boolean, now: number, closesAt?: string): string | null {
  const notHeld = notHeldRoute(a);
  if (notHeld) return notHeld;
  if (sessionHeld(a, therapistJoined, now)) return null;
  const closes = closesAt ? Date.parse(closesAt) : Date.parse(a.startsAt) + a.durationMinutes * 60_000;
  return now <= closes ? `${base(a.id)}/lobby` : base(a.id);
}
