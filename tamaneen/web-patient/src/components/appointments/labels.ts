import type { Locale } from '@/i18n/routing';
import type { Appointment } from '@/lib/api/types';
import { formatNumber } from '@/lib/format';
import type { Translator } from '@/components/matching/labels';

/** "الجلسة الأولى" … "الجلسة العاشرة", then "الجلسة 11"; "First session" … "Session 11". `t` is the "appointments" translator. */
export function sessionLabel(t: Translator, sequenceNumber: number, locale: Locale): string {
  const n = Math.max(1, Math.floor(sequenceNumber));
  return t('session', { n, num: formatNumber(n, locale) });
}

export function isCancelled(status: Appointment['status']): boolean {
  return status === 'cancelled_by_patient' || status === 'cancelled_by_therapist';
}

/** The localized status line: "مؤكد", "ألغته الأخصائية"… `t` is the "appointments" translator. */
export function statusLabel(t: Translator, appointment: Pick<Appointment, 'status' | 'therapist'>): string {
  return t(`status.${appointment.status}`, { gender: appointment.therapist.gender });
}
