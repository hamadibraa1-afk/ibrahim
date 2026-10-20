export type Language = 'ar' | 'en';
export type Direction = 'rtl' | 'ltr';

export const LANGUAGES: readonly Language[] = ['ar', 'en'];
export const DEFAULT_LANGUAGE: Language = 'ar';
export const LANGUAGE_STORAGE_KEY = 'tamaneen.staff.lang';

export function isLanguage(value: unknown): value is Language {
  return value === 'ar' || value === 'en';
}

/** "ar-SA" → "ar", "en_GB" → "en", anything else → null. */
export function toLanguage(value: string | null | undefined): Language | null {
  const neutral = value?.trim().toLowerCase().replace('_', '-').split('-')[0];
  return isLanguage(neutral) ? neutral : null;
}

export function directionOf(language: Language): Direction {
  return language === 'ar' ? 'rtl' : 'ltr';
}

export function otherLanguage(language: Language): Language {
  return language === 'ar' ? 'en' : 'ar';
}
