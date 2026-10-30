import type { Language } from './language';

export type DateStyle = 'longDate' | 'time' | 'timeSeconds' | 'dayTime';

/** Gregorian calendar and Latin digits in both languages, as in the design. */
export function intlLocale(language: Language): string {
  return language === 'ar' ? 'ar-u-ca-gregory-nu-latn' : 'en-GB';
}

const OPTIONS: Record<DateStyle, Intl.DateTimeFormatOptions> = {
  longDate: { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' },
  time: { hour: 'numeric', minute: '2-digit' },
  timeSeconds: { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false },
  dayTime: { weekday: 'long', day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' },
};

const DATE_ONLY = /^\d{4}-\d{2}-\d{2}$/;

/** S32: the zone the staff member chose; undefined shows the browser's. Set at sign-in and when it changes. */
let displayTimeZone: string | undefined;

export function setDisplayTimeZone(timeZone: string | null | undefined): void {
  displayTimeZone = timeZone || undefined;
}

export function getDisplayTimeZone(): string | undefined {
  return displayTimeZone;
}

/** Formats an ISO-8601 value in the portal language. A date without a time ("2026-10-19") is a calendar date, not a UTC instant. */
export function formatDate(value: string | null | undefined, style: DateStyle, language: Language): string {
  if (!value) {
    return '';
  }
  const dateOnly = DATE_ONLY.test(value);
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return '';
  }
  return new Intl.DateTimeFormat(intlLocale(language), { ...OPTIONS[style], ...(dateOnly ? { timeZone: 'UTC' } : displayTimeZone ? { timeZone: displayTimeZone } : {}) }).format(date);
}

/** "mm:ss" for a remaining duration; minutes keep counting past 59, and an elapsed deadline shows 00:00. */
export function formatCountdown(remainingMs: number): string {
  const total = Math.max(0, Math.ceil(remainingMs / 1000));
  const minutes = Math.floor(total / 60);
  const seconds = total % 60;
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`;
}
