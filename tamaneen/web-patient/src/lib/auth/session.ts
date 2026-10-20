import type { NextResponse } from 'next/server';
import type { TokenPair } from '@/lib/api/types';
import { ACCESS_COOKIE, REFRESH_COOKIE, accessCookie, refreshCookie } from './cookies';

export function setSessionCookies(response: NextResponse, tokens: TokenPair): void {
  response.cookies.set(ACCESS_COOKIE, tokens.accessToken, accessCookie(tokens.accessExpiresAt));
  response.cookies.set(REFRESH_COOKIE, tokens.refreshToken, refreshCookie(tokens.refreshExpiresAt));
}

export function clearSessionCookies(response: NextResponse): void {
  response.cookies.set(ACCESS_COOKIE, '', { httpOnly: true, path: '/', maxAge: 0 });
  response.cookies.set(REFRESH_COOKIE, '', { httpOnly: true, path: '/', maxAge: 0 });
}

/** Paths whose responses carry tokens: only the auth route handlers may call them, never the generic proxy. */
export const TOKEN_PATHS = new Set(['auth/signup/verify', 'auth/login', 'auth/login/mfa', 'auth/refresh', 'auth/logout']);
