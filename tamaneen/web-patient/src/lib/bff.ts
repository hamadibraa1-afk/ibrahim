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

export function upstreamHeaders(input: { locale: Locale; accessToken?: string; contentType?: string | null; requestId?: string | null }): Headers {
  const headers = new Headers({ 'Accept-Language': input.locale, Accept: 'application/json' });
  if (input.accessToken) headers.set('Authorization', `Bearer ${input.accessToken}`);
  if (input.contentType) headers.set('Content-Type', input.contentType);
  if (input.requestId) headers.set('X-Request-Id', input.requestId);
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
