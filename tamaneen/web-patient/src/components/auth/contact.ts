import { asciiDigits } from '@/lib/format';

/** Matches the API's rules: passwords need at least 12 characters (the breach list is checked server-side). */
export const MIN_PASSWORD_LENGTH = 12;
export const CODE_LENGTH = 6;
/** Failed sign-ins lock the account for this long (docs/api-v1.md). */
export const LOCK_MINUTES = 15;

const PHONE = /^\+?[0-9]{9,15}$/;
const EMAIL = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** A phone number as typed (any digits, spaces, dashes) → "+9665…" / "05…" with ASCII digits only. */
export function normalizePhone(value: string): string {
  return asciiDigits(value).replace(/[\s\-().]/g, '');
}

export function normalizeEmail(value: string): string {
  return value.trim().toLowerCase();
}

/** Login and recovery accept either: e-mail if it has an @, otherwise a phone number. */
export function normalizeContact(value: string): string {
  const v = asciiDigits(value).trim();
  return v.includes('@') ? normalizeEmail(v) : normalizePhone(v);
}

export function isValidPhone(value: string): boolean {
  return PHONE.test(normalizePhone(value));
}

export function isValidEmail(value: string): boolean {
  return EMAIL.test(normalizeEmail(value));
}

export function isValidContact(value: string): boolean {
  const v = normalizeContact(value);
  return v.includes('@') ? EMAIL.test(v) : PHONE.test(v);
}

/** Six-digit codes: ASCII digits only, at most six. */
export function normalizeCode(value: string): string {
  return asciiDigits(value).replace(/\D/g, '').slice(0, CODE_LENGTH);
}

/** Recovery codes look like "7KQ2-M9XA"; accept lowercase, spaces and Arabic-Indic digits. */
export function normalizeRecoveryCode(value: string): string {
  return asciiDigits(value).toUpperCase().replace(/[^A-Z0-9-]/g, '');
}

/** CLDR plural category for message keys like `attempts.two` (Arabic has six, English two). */
export function pluralKey(locale: string, n: number): 'zero' | 'one' | 'two' | 'few' | 'many' | 'other' {
  return new Intl.PluralRules(locale).select(n) as 'zero' | 'one' | 'two' | 'few' | 'many' | 'other';
}
