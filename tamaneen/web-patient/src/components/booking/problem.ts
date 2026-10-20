import { ApiError } from '@/lib/api/errors';

export const NOT_ELIGIBLE_CODE = 'error.booking.not_eligible';

/** A localized problem to show: the API's own title/detail when it sent one, otherwise the caller's fallback. */
export interface ShownProblem {
  title: string;
  detail?: string;
}

export function toShownProblem(error: unknown, fallback: string): ShownProblem {
  if (error instanceof ApiError && error.problem.title) {
    return { title: error.problem.title, detail: error.problem.detail };
  }
  return { title: fallback };
}

export function isNotEligible(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403 && error.code === NOT_ELIGIBLE_CODE;
}

export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}
