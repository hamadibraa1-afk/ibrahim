import { HttpErrorResponse } from '@angular/common/http';
import type { ApiProblem } from './types';

/**
 * Reads the ProblemDetails body of a failed API call. Returns null when the server could not be reached
 * or the body is not a problem (the caller then shows its own localized fallback).
 */
export function toProblem(error: unknown): ApiProblem | null {
  if (!(error instanceof HttpErrorResponse) || error.status === 0) {
    return null;
  }
  const body: unknown = error.error;
  if (body && typeof body === 'object' && typeof (body as { title?: unknown }).title === 'string') {
    const problem = body as Partial<ApiProblem>;
    return {
      title: problem.title!,
      detail: typeof problem.detail === 'string' ? problem.detail : undefined,
      status: typeof problem.status === 'number' ? problem.status : error.status,
      code: typeof problem.code === 'string' ? problem.code : undefined,
      remainingAttempts: typeof problem.remainingAttempts === 'number' ? problem.remainingAttempts : undefined,
      retryAfterSeconds: typeof problem.retryAfterSeconds === 'number' ? problem.retryAfterSeconds : undefined,
    };
  }
  return null;
}

export function statusOf(error: unknown): number | null {
  return error instanceof HttpErrorResponse ? error.status : null;
}

/** A message ready to show: the server's localized problem, or a translation key for the fallback. */
export interface ShownError {
  title: string | null;
  detail: string | null;
  fallbackKey: string;
}

export function shownError(error: unknown, fallbackKey: string): ShownError {
  const problem = toProblem(error);
  return { title: problem?.title ?? null, detail: problem?.detail ?? null, fallbackKey };
}
