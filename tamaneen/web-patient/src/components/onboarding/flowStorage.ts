import type { ContactType, MfaMethod } from '@/lib/api/types';

/**
 * Short-lived sign-up and sign-in state kept in sessionStorage, so it disappears with the tab. Nothing
 * here is a token: only what the next screen needs to continue. Storage can be unavailable (private
 * mode, blocked storage), so every access is guarded and callers treat a missing value as "start the
 * step again".
 */
export const FLOW_KEYS = {
  eligibility: 'tamaneen.flow.eligibility',
  signup: 'tamaneen.flow.signup',
  recoveryCodes: 'tamaneen.flow.recoveryCodes',
  mfa: 'tamaneen.flow.mfa',
} as const;

export type FlowKey = (typeof FLOW_KEYS)[keyof typeof FLOW_KEYS];

export interface EligibilityState {
  eligibilityToken: string;
}

export interface SignupDraft {
  nickname: string;
  contactType: ContactType;
  contact: string;
}

export interface SignupState extends SignupDraft {
  challengeId: string;
  maskedContact: string;
  resendAfterSeconds: number;
  /** When the code was sent (ms since epoch), so the resend countdown survives a reload. */
  sentAt: number;
}

export interface MfaState {
  mfaToken: string;
  methods: MfaMethod[];
  maskedPhone?: string;
}

function storage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

export function readFlow<T>(key: FlowKey, isValid: (value: unknown) => value is T): T | null {
  try {
    const raw = storage()?.getItem(key);
    if (!raw) return null;
    const value: unknown = JSON.parse(raw);
    return isValid(value) ? value : null;
  } catch {
    return null;
  }
}

export function writeFlow(key: FlowKey, value: unknown): boolean {
  try {
    const s = storage();
    if (!s) return false;
    s.setItem(key, JSON.stringify(value));
    return true;
  } catch {
    return false;
  }
}

export function clearFlow(...keys: FlowKey[]): void {
  try {
    const s = storage();
    for (const key of keys) s?.removeItem(key);
  } catch {
    // Nothing to clear when storage is unavailable.
  }
}

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null;
const isString = (v: unknown): v is string => typeof v === 'string' && v.length > 0;

export function isEligibilityState(v: unknown): v is EligibilityState {
  return isObject(v) && isString(v.eligibilityToken);
}

export function isSignupDraft(v: unknown): v is SignupDraft {
  return isObject(v) && typeof v.nickname === 'string' && (v.contactType === 'phone' || v.contactType === 'email') && typeof v.contact === 'string';
}

export function isSignupState(v: unknown): v is SignupState {
  return isSignupDraft(v) && isObject(v) && isString(v.challengeId) && typeof v.maskedContact === 'string' && typeof v.resendAfterSeconds === 'number' && typeof v.sentAt === 'number';
}

export function isRecoveryCodes(v: unknown): v is string[] {
  return Array.isArray(v) && v.length > 0 && v.every(isString);
}

export function isMfaState(v: unknown): v is MfaState {
  return (
    isObject(v) &&
    isString(v.mfaToken) &&
    Array.isArray(v.methods) &&
    v.methods.every((m) => m === 'totp' || m === 'sms' || m === 'recovery') &&
    (v.maskedPhone === undefined || typeof v.maskedPhone === 'string')
  );
}
