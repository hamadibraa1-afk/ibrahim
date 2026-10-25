import { clearAllDrafts } from './drafts';

/** The service worker's runtime cache (public/sw.js RUNTIME): pages kept for offline use after a visit. */
const RUNTIME_PREFIX = 'tamaneen-runtime-';

/**
 * Before leaving on sign-out (PB-61): delete the pages the service worker stored while signed in and the
 * unsent drafts. The precache (public crisis resources and offline pages) stays, so emergency numbers keep
 * working offline. Never throws: sign-out must go ahead even when storage is blocked.
 */
export async function clearLocalCopies(): Promise<void> {
  clearAllDrafts();
  try {
    if (typeof caches === 'undefined') return;
    const keys = await caches.keys();
    await Promise.all(keys.filter((key) => key.startsWith(RUNTIME_PREFIX)).map((key) => caches.delete(key)));
  } catch {
    // CacheStorage unavailable (private window, old browser): there is nothing stored to clear.
  }
}
