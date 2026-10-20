import type { SafetyPlan } from '@/lib/api/types';

export const SAFETY_PLAN_KEY = 'tamaneen.safetyPlan.v1';

export interface CachedSafetyPlan {
  locale: string;
  savedAt: string;
  plan: SafetyPlan;
}

function isPlan(value: unknown): value is SafetyPlan {
  return typeof value === 'object' && value !== null && typeof (value as SafetyPlan).sections === 'object' && (value as SafetyPlan).sections !== null;
}

/** The last plan fetched on this device, so P37 still opens offline. Returns null when absent or unreadable. */
export function readCachedPlan(): CachedSafetyPlan | null {
  try {
    const raw = window.localStorage.getItem(SAFETY_PLAN_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as Partial<CachedSafetyPlan>;
    return isPlan(parsed.plan) && typeof parsed.savedAt === 'string' ? { locale: String(parsed.locale ?? ''), savedAt: parsed.savedAt, plan: parsed.plan } : null;
  } catch {
    return null;
  }
}

export function writeCachedPlan(plan: SafetyPlan, locale: string, now = new Date()): void {
  try {
    window.localStorage.setItem(SAFETY_PLAN_KEY, JSON.stringify({ locale, savedAt: now.toISOString(), plan } satisfies CachedSafetyPlan));
  } catch {
    // Storage full or blocked: the plan still shows from the network.
  }
}

/** For sign-out: the plan must not stay on a shared device. */
export function clearCachedPlan(): void {
  try {
    window.localStorage.removeItem(SAFETY_PLAN_KEY);
  } catch {
    // Nothing stored.
  }
}
