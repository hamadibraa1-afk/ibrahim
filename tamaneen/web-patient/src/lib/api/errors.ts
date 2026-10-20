import type { ApiProblem } from './types';

export const CRISIS_LOCKED_CODE = 'error.crisis.locked';

/** A failed API call. `problem` is the API's localized ProblemDetails, safe to show as-is. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ApiProblem,
  ) {
    super(problem.detail ?? problem.title);
    this.name = 'ApiError';
  }

  get code(): string | undefined {
    return this.problem.code;
  }

  /** Field errors from a validation problem, keyed by camelCase field name. */
  fieldErrors(): Record<string, string> {
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(this.problem.errors ?? {})) {
      const first = v[0];
      if (first) out[k.charAt(0).toLowerCase() + k.slice(1)] = first;
    }
    return out;
  }
}

export async function toApiError(response: Response): Promise<ApiError> {
  let problem: ApiProblem;
  try {
    problem = (await response.json()) as ApiProblem;
  } catch {
    problem = { title: response.statusText || 'Error', status: response.status };
  }
  return new ApiError(response.status, { ...problem, status: problem.status ?? response.status });
}
