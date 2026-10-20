/** HttpOnly cookies that hold the session. Only the BFF and middleware read them; client code cannot. */
export const ACCESS_COOKIE = 'tamaneen_at';
export const REFRESH_COOKIE = 'tamaneen_rt';
export const DEVICE_COOKIE = 'tamaneen_dt';

export interface CookieOptions {
  httpOnly: true;
  secure: boolean;
  sameSite: 'strict' | 'lax';
  path: string;
  maxAge: number;
}

/** Secure in production unless explicitly turned off for a plain-http local trial (TAMANEEN_SECURE_COOKIES=false). */
const secure = () => {
  const flag = process.env.TAMANEEN_SECURE_COOKIES;
  if (flag === 'true' || flag === 'false') return flag === 'true';
  return process.env.NODE_ENV === 'production';
};

export function secondsUntil(iso: string, now = Date.now()): number {
  return Math.max(0, Math.floor((Date.parse(iso) - now) / 1000));
}

export function accessCookie(expiresAt: string): CookieOptions {
  // Lax so the first navigation from a link (e.g. a reminder) is still signed in; API calls are same-origin.
  return { httpOnly: true, secure: secure(), sameSite: 'lax', path: '/', maxAge: secondsUntil(expiresAt) };
}

export function refreshCookie(expiresAt: string): CookieOptions {
  return { httpOnly: true, secure: secure(), sameSite: 'lax', path: '/', maxAge: secondsUntil(expiresAt) };
}

export function deviceCookie(): CookieOptions {
  return { httpOnly: true, secure: secure(), sameSite: 'strict', path: '/api/auth', maxAge: 30 * 24 * 3600 };
}
