import type { Locale } from '@/i18n/routing';
import { DEFAULT_TIME_ZONE, formatDate, formatRelativeDays, formatTime } from '@/lib/format';

const tag = (locale: Locale) => (locale === 'ar' ? 'ar-u-nu-latn' : 'en-GB');

const steps: [Intl.RelativeTimeFormatUnit, number][] = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 7],
  ['week', 4.35],
  ['month', 12],
  ['year', Number.POSITIVE_INFINITY],
];

/** "3 weeks ago", "قبل 3 أسابيع", "now": the largest unit that fits, Latin digits in both languages. */
export function formatAgo(iso: string, locale: Locale, now = new Date()): string {
  let value = (Date.parse(iso) - now.getTime()) / 1000;
  const rtf = new Intl.RelativeTimeFormat(tag(locale), { numeric: 'auto' });
  for (const [unit, size] of steps) {
    if (Math.abs(value) < size) return rtf.format(Math.round(value), unit);
    value /= size;
  }
  return rtf.format(Math.round(value), 'year');
}

/** "today 9:12 AM", "أمس 3:40 م" within the last week, otherwise the date and time. */
export function formatDayAndTime(iso: string, locale: Locale, now = new Date(), timeZone = DEFAULT_TIME_ZONE): string {
  const days = Math.abs((now.getTime() - Date.parse(iso)) / 86_400_000);
  const day = days < 6 ? formatRelativeDays(iso, locale, now, timeZone) : formatDate(iso, locale, timeZone, { day: 'numeric', month: 'long' });
  const text = `${day} ${formatTime(iso, locale, timeZone)}`;
  return locale === 'en' ? text.charAt(0).toUpperCase() + text.slice(1) : text;
}

/** A wall-clock "HH:mm" (as stored in notification preferences) shown as "9:00 PM" / "9:00 م". */
export function formatClock(hhmm: string, locale: Locale): string {
  const [h = '0', m = '0'] = hhmm.split(':');
  const iso = `1970-01-01T${h.padStart(2, '0')}:${m.padStart(2, '0')}:00Z`;
  return formatTime(iso, locale, 'UTC');
}

/** The calendar day (YYYY-MM-DD) of an instant in a time zone. */
export function dayKey(date: Date, timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date);
}

/** The hour (0–23) of an instant in a time zone. */
export function hourIn(date: Date, timeZone = DEFAULT_TIME_ZONE): number {
  return Number(new Intl.DateTimeFormat('en-GB', { timeZone, hour: '2-digit', hourCycle: 'h23' }).format(date));
}
