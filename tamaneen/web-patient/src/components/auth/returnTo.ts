/**
 * The last signed-in screen of this tab, so sign-in after an expired session returns there (P18 «آخر
 * مسار»). Kept in sessionStorage with the pseudonym of the patient who was on it: it never leaves the tab,
 * it is read once and cleared, and another patient signing in on the same tab never gets it. Only paths
 * of this app are accepted (no scheme, no host, no "//"), without the locale prefix that the router adds.
 */
const KEY = 'tamaneen.lastRoute';
const PATH = /^\/[A-Za-z0-9\-_/]*(\?[A-Za-z0-9\-_=&%.]*)?$/;
/** Screens it makes no sense to return to after signing in. */
const SKIP = /^\/(start|login|recover|session-expired|offline|maintenance)(\/|\?|$)/;

/** A same-app path without the locale, or null when the value is not one. */
export function safeRoute(raw: string | null | undefined): string | null {
  if (!raw || raw.startsWith('//') || !PATH.test(raw)) return null;
  const route = raw.replace(/^\/(ar|en)(?=\/|\?|$)/, '') || '/';
  return route === '/' || SKIP.test(route) ? null : route;
}

function storage(): Storage | null {
  try {
    return typeof window === 'undefined' ? null : window.sessionStorage;
  } catch {
    return null;
  }
}

export function rememberRoute(path: string, pseudonymId: string): void {
  const route = safeRoute(path);
  if (!route) return;
  try {
    storage()?.setItem(KEY, JSON.stringify({ route, pseudonymId }));
  } catch {
    // Storage blocked: sign-in falls back to the landing screen.
  }
}

/** The route this patient was on, once; cleared so a later sign-in in this tab does not reuse it. */
export function takeReturnRoute(pseudonymId: string): string | null {
  try {
    const s = storage();
    const raw = s?.getItem(KEY);
    s?.removeItem(KEY);
    const saved: unknown = raw ? JSON.parse(raw) : null;
    if (typeof saved !== 'object' || saved === null) return null;
    const { route, pseudonymId: owner } = saved as Record<string, unknown>;
    return owner === pseudonymId && typeof route === 'string' ? safeRoute(route) : null;
  } catch {
    return null;
  }
}
