import { isLocale, routing, type Locale } from '@/i18n/routing';

/** Cookie holding the access token. HttpOnly, Secure, SameSite=Strict; set by the sign-in flow. */
export const ACCESS_TOKEN_COOKIE = 'tamaneen_at';
export const LOCALE_COOKIE = 'NEXT_LOCALE';

const SAFE_SEGMENT = /^[A-Za-z0-9._~-]+$/;
const FORWARDED_RESPONSE_HEADERS = ['content-type', 'content-language', 'content-disposition', 'cache-control', 'retry-after', 'etag'];

/** The language the API should answer in: the user's saved choice, then the browser's, then Arabic. */
export function resolveLocale(cookieLocale: string | undefined, acceptLanguage: string | null): Locale {
  if (isLocale(cookieLocale)) return cookieLocale;
  for (const part of (acceptLanguage ?? '').split(',')) {
    const tag = part.split(';')[0]?.trim().toLowerCase().split('-')[0];
    if (isLocale(tag)) return tag;
  }
  return routing.defaultLocale;
}

/** Builds the upstream API URL. Rejects anything that could escape /v1 (.., encoded slashes, empty parts). */
export function upstreamUrl(apiBase: string, segments: string[], search: string): URL {
  if (segments.length === 0 || segments.some((s) => !SAFE_SEGMENT.test(s) || s === '.' || s === '..')) {
    throw new RangeError('Invalid API path');
  }
  const base = apiBase.endsWith('/') ? apiBase : `${apiBase}/`;
  const url = new URL(`v1/${segments.join('/')}`, base);
  url.search = search;
  return url;
}

/** If-Match passes through, so the API can refuse a save based on stale settings (412, PB-52). */
export function upstreamHeaders(input: { locale: Locale; accessToken?: string; contentType?: string | null; requestId?: string | null; ifMatch?: string | null }): Headers {
  const headers = new Headers({ 'Accept-Language': input.locale, Accept: 'application/json' });
  if (input.accessToken) headers.set('Authorization', `Bearer ${input.accessToken}`);
  if (input.contentType) headers.set('Content-Type', input.contentType);
  if (input.requestId) headers.set('X-Request-Id', input.requestId);
  if (input.ifMatch) headers.set('If-Match', input.ifMatch);
  return headers;
}

export function downstreamHeaders(upstream: Headers): Headers {
  const headers = new Headers();
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.get(name);
    if (value) headers.set(name, value);
  }
  return headers;
}

/**
 * CSRF check for cookie-authenticated writes: a browser's Origin must name this site. The site is the
 * configured public origin (TAMANEEN_PUBLIC_ORIGIN) or, failing that, the Host the browser addressed.
 * request.nextUrl is not used because behind a container or proxy it carries the bind address (0.0.0.0).
 * Requests without an Origin (same-origin GET, server-side callers) pass.
 */
export function isSameOrigin(origin: string | null, host: string | null, publicOrigin = process.env.TAMANEEN_PUBLIC_ORIGIN): boolean {
  if (!origin) return true;
  let parsed: URL;
  try {
    parsed = new URL(origin);
  } catch {
    return false;
  }
  if (publicOrigin) return parsed.origin === new URL(publicOrigin).origin;
  return host !== null && parsed.host === host;
}
