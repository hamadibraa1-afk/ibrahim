import type { Locale } from '@/i18n/routing';

/** The pilot region's zone; a patient's own zone (from /v1/me) overrides it where available. */
export const DEFAULT_TIME_ZONE = 'Asia/Riyadh';

// Latin digits in both languages, matching the design file ("الأربعاء 21 أكتوبر · 4:00 م").
const tag = (locale: Locale) => (locale === 'ar' ? 'ar-u-nu-latn' : 'en-GB');

export function formatDate(iso: string, locale: Locale, timeZone = DEFAULT_TIME_ZONE, opts: Intl.DateTimeFormatOptions = { weekday: 'long', day: 'numeric', month: 'long' }): string {
  return new Intl.DateTimeFormat(tag(locale), { timeZone, ...opts }).format(new Date(iso));
}

export function formatTime(iso: string, locale: Locale, timeZone = DEFAULT_TIME_ZONE): string {
  return new Intl.DateTimeFormat(locale === 'ar' ? 'ar-u-nu-latn' : 'en-US', { timeZone, hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
}

export function formatDateTime(iso: string, locale: Locale, timeZone = DEFAULT_TIME_ZONE): string {
  return `${formatDate(iso, locale, timeZone)} · ${formatTime(iso, locale, timeZone)}`;
}

export function formatNumber(n: number, locale: Locale): string {
  return new Intl.NumberFormat(tag(locale)).format(n);
}

/** "in 2 days", "tomorrow", "بعد يومين", "غدًا". */
export function formatRelativeDays(iso: string, locale: Locale, now = new Date(), timeZone = DEFAULT_TIME_ZONE): string {
  const day = (d: Date) => new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(d);
  const diff = Math.round((Date.parse(day(new Date(iso))) - Date.parse(day(now))) / 86_400_000);
  return new Intl.RelativeTimeFormat(tag(locale), { numeric: 'auto' }).format(diff, 'day');
}

/** Digits a patient may type in Arabic-Indic or Persian form become ASCII before sending. */
export function asciiDigits(value: string): string {
  return value.replace(/[٠-٩]/g, (d) => String(d.charCodeAt(0) - 0x0660)).replace(/[۰-۹]/g, (d) => String(d.charCodeAt(0) - 0x06f0));
}
