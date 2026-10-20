import { ApiError } from '@/lib/api/errors';

/** The API's localized problem text (detail, else title), or the given fallback for network failures. */
export function problemText(error: unknown, fallback: string): string {
  if (error instanceof ApiError) return error.problem.detail || error.problem.title || fallback;
  return fallback;
}

/** Validation errors keyed by field, or none when the error is not a validation problem. */
export function fieldErrorsOf(error: unknown): Record<string, string> {
  return error instanceof ApiError ? error.fieldErrors() : {};
}
