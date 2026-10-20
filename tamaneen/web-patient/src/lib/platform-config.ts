import 'server-only';
import { publicGet } from '@/lib/api/server';
import type { PublicConfig } from '@/lib/api/types';

/**
 * Values the platform admin manages in Platform Settings (S43). The live values come from
 * GET /v1/meta/config; the deployment also provides a safety snapshot through environment variables,
 * which is the documented fallback when the settings service is unreachable. Crisis screens must always
 * be able to show a number, so they read through `loadSafetyNumbers()`, never the API alone.
 */
export interface PublicPlatformConfig {
  emergencyNumber: string | null;
  supportLine: string | null;
  matchWithinHours: number;
  triageWithinHours: number;
  /** ISO time the scheduled maintenance should end (P35), or null when unknown. */
  maintenanceUntil: string | null;
}

const PHONE = /^\+?[0-9]{2,15}$/;

function phone(raw: string | null | undefined): string | null {
  const value = raw?.replace(/[\s-]/g, '').trim();
  return value && PHONE.test(value) ? value : null;
}

function hours(raw: string | number | null | undefined, fallback: number): number {
  const n = typeof raw === 'number' ? raw : Number.parseInt(raw ?? '', 10);
  return Number.isFinite(n) && n > 0 && n <= 720 ? Math.round(n) : fallback;
}

function isoTime(raw: string | undefined): string | null {
  const value = raw?.trim();
  return value && !Number.isNaN(Date.parse(value)) ? new Date(value).toISOString() : null;
}

export function publicPlatformConfig(env: NodeJS.ProcessEnv = process.env): PublicPlatformConfig {
  return {
    emergencyNumber: phone(env.TAMANEEN_EMERGENCY_NUMBER),
    supportLine: phone(env.TAMANEEN_SUPPORT_LINE),
    matchWithinHours: hours(env.TAMANEEN_MATCH_WITHIN_HOURS, 72),
    triageWithinHours: hours(env.TAMANEEN_TRIAGE_WITHIN_HOURS, 24),
    maintenanceUntil: isoTime(env.TAMANEEN_MAINTENANCE_UNTIL),
  };
}

export interface SafetyNumbers {
  emergencyNumber: string | null;
  supportLine: string | null;
  triageWithinHours: number;
}

/** Prefers the live PublicConfig; any missing or malformed value falls back to the environment snapshot. */
export function safetyNumbers(config: Partial<PublicConfig> | null, env: NodeJS.ProcessEnv = process.env): SafetyNumbers {
  const fallback = publicPlatformConfig(env);
  return {
    emergencyNumber: phone(config?.emergencyNumber) ?? fallback.emergencyNumber,
    supportLine: phone(config?.supportLine) ?? fallback.supportLine,
    triageWithinHours: hours(config?.triageWithinHours, fallback.triageWithinHours),
  };
}

/** Server components: the numbers to render as tel: links. Never throws (publicGet returns null when the API is down). */
export async function loadSafetyNumbers(): Promise<SafetyNumbers> {
  return safetyNumbers(await publicGet<PublicConfig>('meta/config'));
}
