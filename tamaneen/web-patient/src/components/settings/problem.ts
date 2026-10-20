import { ApiError } from '@/lib/api/errors';

/** The API's own localized explanation of a failed call, or the given fallback (network errors, aborted calls). */
export function problemMessage(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem.detail ?? error.problem.title;
  return fallback;
}

/** Validation errors keyed by field (camelCase), empty for anything else. */
export function fieldProblems(error: unknown): Record<string, string> {
  return error instanceof ApiError ? error.fieldErrors() : {};
}
