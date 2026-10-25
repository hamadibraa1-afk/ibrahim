'use client';

import { rememberReturnPath, sessionExpiredUrl } from '@/lib/returnPath';
import { ApiError, CRISIS_LOCKED_CODE, MAINTENANCE_CODE, toApiError } from './errors';

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

let refreshing: Promise<boolean> | null = null;

/** One refresh at a time, shared by every call that hit a 401 at once. */
function refreshSession(): Promise<boolean> {
  refreshing ??= fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' })
    .then((r) => r.ok)
    .catch(() => false)
    .finally(() => {
      refreshing = null;
    });
  return refreshing;
}

function currentLocale(): string {
  return typeof document !== 'undefined' ? document.documentElement.lang || 'ar' : 'ar';
}

type ApiOptions = { method?: Method; body?: unknown; signal?: AbortSignal; ifMatch?: string | null };

/**
 * Calls the API through the BFF (/api/v1/...). The session cookie is attached server-side; this code never
 * sees a token. A 401 triggers one silent refresh. If the session is gone the user lands on the
 * session-expired page, and if the chat is locked by a crisis event, on the crisis screen.
 */
async function send(path: string, options: ApiOptions): Promise<Response> {
  const url = `/api/v1/${path.replace(/^\/+/, '')}`;
  const headers: Record<string, string> = { Accept: 'application/json' };
  if (options.body !== undefined) headers['Content-Type'] = 'application/json';
  if (options.ifMatch) headers['If-Match'] = options.ifMatch;
  const init: RequestInit = {
    method: options.method ?? 'GET',
    credentials: 'same-origin',
    headers,
    body: options.body === undefined ? undefined : JSON.stringify(options.body),
    signal: options.signal,
  };

  let response = await fetch(url, init);
  if (response.status === 401 && (await refreshSession())) {
    response = await fetch(url, init);
  }

  if (!response.ok) {
    const error = await toApiError(response);
    if (typeof window !== 'undefined') {
      if (response.status === 401) {
        const here = `${window.location.pathname}${window.location.search}`;
        rememberReturnPath(here);
        window.location.assign(sessionExpiredUrl(currentLocale(), here));
      } else if (error.code === MAINTENANCE_CODE) {
        window.location.assign(`/${currentLocale()}/maintenance`);
      } else if (error.code === CRISIS_LOCKED_CODE) {
        window.location.assign(`/${currentLocale()}/crisis`);
      }
    }
    throw error;
  }
  return response;
}

async function bodyOf<T>(response: Response): Promise<T> {
  if (response.status === 204 || response.headers.get('content-length') === '0') {
    return undefined as T;
  }
  return (await response.json()) as T;
}

export async function api<T>(path: string, options: ApiOptions = {}): Promise<T> {
  return bodyOf<T>(await send(path, options));
}

/**
 * Like api(), and also returns the response's ETag. The settings screens (P21, P31, P34) send it back as
 * `ifMatch` when saving; a 412 (isVersionConflict) means another device saved first.
 */
export async function apiVersioned<T>(path: string, options: ApiOptions = {}): Promise<{ data: T; etag: string | null }> {
  const response = await send(path, options);
  return { data: await bodyOf<T>(response), etag: response.headers.get('etag') };
}

/** 412: the values changed since this screen read them. */
export function isVersionConflict(e: unknown): boolean {
  return e instanceof ApiError && e.status === 412;
}

/** Calls one of the BFF auth routes (/api/auth/...), which keep tokens in HttpOnly cookies. */
export async function authApi<T>(action: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api/auth/${action}`, {
    method: 'POST',
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', Accept: 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  if (!response.ok) throw await toApiError(response);
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

export { ApiError };
