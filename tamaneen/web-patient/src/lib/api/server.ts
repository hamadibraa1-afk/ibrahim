import 'server-only';
import { cookies, headers } from 'next/headers';
import { redirect } from 'next/navigation';
import { cache } from 'react';
import { getLocale } from 'next-intl/server';
import { isLocale, routing, type Locale } from '@/i18n/routing';
import { ACCESS_COOKIE } from '@/lib/auth/cookies';
import { PATH_HEADER } from '@/lib/pathHeader';
import { sessionExpiredUrl } from '@/lib/returnPath';
import { ApiError, CRISIS_LOCKED_CODE, MAINTENANCE_CODE, toApiError } from './errors';
import type { Me } from './types';
import { callApi } from './upstream';

async function requestLocale(): Promise<Locale> {
  const locale = await getLocale();
  return isLocale(locale) ? locale : routing.defaultLocale;
}

/**
 * GET from a server component with the session cookie. 401 → session-expired page (the middleware has
 * already tried a refresh), crisis lock → crisis screen. Other errors throw ApiError for error.tsx.
 */
export async function serverGet<T>(path: string, onResponse?: (response: Response) => void): Promise<T> {
  const locale = await requestLocale();
  const jar = await cookies();
  const requestHeaders = await headers();
  // The middleware passes the page path along, so the patient can come back to it after signing in (P35).
  const expired = sessionExpiredUrl(locale, requestHeaders.get(PATH_HEADER));
  const token = jar.get(ACCESS_COOKIE)?.value;
  if (!token) redirect(expired);
  const response = await callApi(`v1/${path.replace(/^\/+/, '')}`, { locale, accessToken: token, requestId: requestHeaders.get('x-request-id') });
  if (response.status === 401) redirect(expired);
  if (!response.ok) {
    const error = await toApiError(response);
    if (error.code === CRISIS_LOCKED_CODE) redirect(`/${locale}/crisis`);
    if (error.code === MAINTENANCE_CODE) redirect(`/${locale}/maintenance`);
    throw error;
  }
  onResponse?.(response);
  return (await response.json()) as T;
}

/** serverGet that also returns the ETag, for the settings forms that save with If-Match (P21, P31, P34). */
export async function serverGetVersioned<T>(path: string): Promise<{ data: T; etag: string | null }> {
  let etag: string | null = null;
  const data = await serverGet<T>(path, (response) => {
    etag = response.headers.get('etag');
  });
  return { data, etag };
}

/** Like serverGet but returns null on 404 instead of throwing. */
export async function serverGetOptional<T>(path: string): Promise<T | null> {
  try {
    return await serverGet<T>(path);
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null;
    throw e;
  }
}

/** The signed-in patient, fetched once per request. */
export const getMe = cache(() => serverGet<Me>('me'));

/** Public GETs (no session needed), e.g. /v1/meta/config. Returns null when the API is unreachable. */
export async function publicGet<T>(path: string): Promise<T | null> {
  try {
    const response = await callApi(`v1/${path.replace(/^\/+/, '')}`, { locale: await requestLocale() });
    return response.ok ? ((await response.json()) as T) : null;
  } catch {
    return null;
  }
}

export async function hasSession(): Promise<boolean> {
  return Boolean((await cookies()).get(ACCESS_COOKIE)?.value);
}
