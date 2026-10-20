import createMiddleware from 'next-intl/middleware';
import { NextResponse, type NextRequest } from 'next/server';
import { routing } from './i18n/routing';
import type { TokenPair } from './lib/api/types';
import { ACCESS_COOKIE, REFRESH_COOKIE, accessCookie, refreshCookie } from './lib/auth/cookies';
import { resolveLocale, upstreamHeaders, LOCALE_COOKIE } from './lib/bff';

// Picks the language from the URL, then the NEXT_LOCALE cookie, then Accept-Language, then Arabic.
const intl = createMiddleware(routing);

/**
 * When the short-lived access cookie has expired but the refresh cookie is still there, rotate the pair
 * before the page renders, so server components see a valid session. One call per navigation at most.
 */
async function refreshed(request: NextRequest): Promise<TokenPair | null> {
  const base = process.env.TAMANEEN_API_URL;
  const refreshToken = request.cookies.get(REFRESH_COOKIE)?.value;
  if (!base || !refreshToken || request.cookies.get(ACCESS_COOKIE)?.value) return null;
  try {
    const locale = resolveLocale(request.cookies.get(LOCALE_COOKIE)?.value, request.headers.get('accept-language'));
    const headers = upstreamHeaders({ locale, contentType: 'application/json' });
    const response = await fetch(new URL('v1/auth/refresh', base.endsWith('/') ? base : `${base}/`), {
      method: 'POST',
      headers,
      body: JSON.stringify({ refreshToken }),
      cache: 'no-store',
      signal: AbortSignal.timeout(5000),
    });
    return response.ok ? ((await response.json()) as TokenPair) : null;
  } catch {
    return null;
  }
}

export default async function middleware(request: NextRequest) {
  const tokens = await refreshed(request);
  if (tokens) {
    // Reload the same URL with the rotated cookies, so the page renders with a valid session.
    const response = NextResponse.redirect(request.nextUrl, 307);
    response.cookies.set(ACCESS_COOKIE, tokens.accessToken, accessCookie(tokens.accessExpiresAt));
    response.cookies.set(REFRESH_COOKIE, tokens.refreshToken, refreshCookie(tokens.refreshExpiresAt));
    return response;
  }
  return intl(request);
}

export const config = {
  matcher: ['/((?!api|_next|_vercel|.*\\..*).*)'],
};
