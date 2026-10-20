import type { Gender, Lang } from '@/lib/api/types';
import type { Locale } from '@/i18n/routing';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

/** The parts of a next-intl translator these helpers use, so client (useTranslations) and server (getTranslations) both fit. */
export interface Translator {
  (key: string, values?: Record<string, string | number>): string;
  has(key: string): boolean;
}

/** A specialty code (anxiety, work_stress…) as a label from the "specialties" namespace; unknown codes are dropped. */
export function specialtyLabels(t: Translator, codes: readonly string[]): string[] {
  return codes.filter((code) => t.has(code)).map((code) => t(code));
}

/** "القلق ومشكلات النوم" / "anxiety and sleep problems". */
export function joinList(items: readonly string[], locale: Locale): string {
  return new Intl.ListFormat(locale === 'ar' ? 'ar' : 'en', { type: 'conjunction' }).format(items);
}

export function isLang(value: string | null | undefined): value is Lang {
  return value === 'ar' || value === 'en';
}

/** The locale as next-intl reports it, narrowed to the app's two languages. */
export function asLocale(value: string): Locale {
  return value === 'en' ? 'en' : 'ar';
}

/** The patient's zone from /v1/me, or the pilot region's when it is missing. */
export function timeZoneOf(me: { timeZone?: string | null }): string {
  return me.timeZone || DEFAULT_TIME_ZONE;
}

/**
 * Gendered copy lives in sibling keys ("labelFemale" / "labelMale") rather than ICU select, so Arabic
 * agreement (اقترحناها / اقترحناه) stays plain text for translators.
 */
export function genderKey(base: string, gender: Gender): string {
  return `${base}${gender === 'female' ? 'Female' : 'Male'}`;
}

/** Count copy in sibling keys: exact Zero/One/Two, then the language's plural category (Few/Many/Other). */
export function countKey(base: string, count: number, locale: Locale): string {
  if (count === 0) return `${base}Zero`;
  if (count === 1) return `${base}One`;
  if (count === 2) return `${base}Two`;
  const category = new Intl.PluralRules(locale).select(count);
  return `${base}${category === 'few' ? 'Few' : category === 'many' ? 'Many' : 'Other'}`;
}
