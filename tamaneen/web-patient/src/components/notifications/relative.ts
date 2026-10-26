import type { Locale } from '@/i18n/routing';

/** «قبل 10 دقائق» / "10 minutes ago", «أمس» / "yesterday"; older than a week shows the date. */
export function relativeTime(iso: string, locale: Locale, now = new Date()): string {
  const diff = (Date.parse(iso) - now.getTime()) / 1000;
  const rtf = new Intl.RelativeTimeFormat(locale === 'ar' ? 'ar-u-nu-latn' : 'en', { numeric: 'auto' });
  const abs = Math.abs(diff);
  if (abs < 60) return rtf.format(0, 'second');
  if (abs < 3600) return rtf.format(Math.round(diff / 60), 'minute');
  if (abs < 86400) return rtf.format(Math.round(diff / 3600), 'hour');
  if (abs < 7 * 86400) return rtf.format(Math.round(diff / 86400), 'day');
  return new Intl.DateTimeFormat(locale === 'ar' ? 'ar-u-nu-latn' : 'en', { day: 'numeric', month: 'long', timeZone: 'Asia/Riyadh' }).format(new Date(iso));
}
