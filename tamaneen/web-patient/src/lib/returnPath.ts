import { routing } from '@/i18n/routing';

/**
 * P35 «تسجيل الدخول… ثم يعيد المسودة والمسار»: where to send a patient back after signing in again.
 * Only same-site, signed-in paths are kept (no open redirects, no loops back to sign-in pages), without the
 * locale prefix so the localized router adds the current one. Shared by the session-expired page, the API
 * clients and the sign-in finish (components/auth/finishSignIn).
 */
const KEY = 'tamaneen.returnTo';
const TTL_MS = 30 * 60 * 1000;
const MAX_LENGTH = 300;
/** Public pages that never make sense to return to after signing in. */
const NOT_A_RETURN = ['/', '/login', '/recover', '/session-expired', '/offline', '/maintenance', '/start/signup', '/start/verify', '/start/eligibility', '/start/recovery-codes'];

/** A validated relative path (with its query) or null. Accepts "/ar/appointments?x=1" and returns "/appointments?x=1". */
export function safeReturnPath(raw: string | null | undefined): string | null {
  if (!raw || raw.length > MAX_LENGTH || !raw.startsWith('/') || raw.startsWith('//') || raw.includes('\\') || /[\u0000-\u001f]/.test(raw)) return null;
  let url: URL;
  try {
    url = new URL(raw, 'https://return.invalid');
  } catch {
    return null;
  }
  if (url.origin !== 'https://return.invalid') return null;
  const parts = url.pathname.split('/');
  const path = (routing.locales as readonly string[]).includes(parts[1] ?? '') ? `/${parts.slice(2).join('/')}` : url.pathname;
  const clean = path.length > 1 ? path.replace(/\/+$/, '') : path;
  if (NOT_A_RETURN.includes(clean) || clean.startsWith('/login/')) return null;
  return `${clean}${url.search}`;
}

/** Keeps the path for this tab, so it survives the MFA step (P19). Storage errors are ignored. */
export function rememberReturnPath(raw: string | null | undefined, now = Date.now()): void {
  const path = safeReturnPath(raw);
  if (!path) return;
  try {
    window.sessionStorage.setItem(KEY, JSON.stringify({ path, at: now }));
  } catch {
    // Storage blocked: the patient lands on the usual screen instead.
  }
}

/** The remembered path, once: reading it removes it. */
export function takeReturnPath(now = Date.now()): string | null {
  try {
    const raw = window.sessionStorage.getItem(KEY);
    window.sessionStorage.removeItem(KEY);
    if (!raw) return null;
    const saved = JSON.parse(raw) as { path?: unknown; at?: unknown };
    if (typeof saved.path !== 'string' || typeof saved.at !== 'number' || now - saved.at > TTL_MS) return null;
    return safeReturnPath(saved.path);
  } catch {
    return null;
  }
}

/** The session-expired URL that carries the current path, e.g. "/ar/session-expired?next=%2Fappointments". */
export function sessionExpiredUrl(locale: string, currentPath: string | null | undefined): string {
  const next = safeReturnPath(currentPath);
  return `/${locale}/session-expired${next ? `?next=${encodeURIComponent(next)}` : ''}`;
}
