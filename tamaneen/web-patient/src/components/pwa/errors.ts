import { ApiError } from '@/lib/api/errors';

/** The API's localized message for a failed call, or null when the failure was not an API answer. */
export function apiMessage(error: unknown): string | null {
  return error instanceof ApiError ? (error.problem.detail ?? error.problem.title) : null;
}

/** fetch() rejects with a TypeError when the network is unreachable (offline, DNS, dropped connection). */
export function isNetworkError(error: unknown): boolean {
  return error instanceof TypeError;
}

/** Gated endpoints that answer with a plain resource may still carry `crisis: true` when the gate blocked. */
export function isCrisis(response: unknown): boolean {
  return typeof response === 'object' && response !== null && (response as { crisis?: unknown }).crisis === true;
}
