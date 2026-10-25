import { clearAllDrafts } from './drafts';
import { clearOutbox } from './outbox';

/** The service worker's runtime cache (public/sw.js RUNTIME): pages kept for offline use after a visit. */
const RUNTIME_PREFIX = 'tamaneen-runtime-';

/**
 * Before leaving on sign-out (PB-61) or after deleting the account: delete the pages the service worker stored
 * while signed in, the unsent drafts and the outbox (P66), so nothing the patient wrote stays for the next person
 * on the device and no queued write is ever sent from someone else's session. The precache (public crisis
 * resources and offline pages) stays, so emergency numbers keep working offline. Never throws: sign-out must go
 * ahead even when storage is blocked.
 */
export async function clearLocalCopies(): Promise<void> {
  clearAllDrafts();
  await clearOutbox();
  try {
    if (typeof caches === 'undefined') return;
    const keys = await caches.keys();
    await Promise.all(keys.filter((key) => key.startsWith(RUNTIME_PREFIX)).map((key) => caches.delete(key)));
  } catch {
    // CacheStorage unavailable (private window, old browser): there is nothing stored to clear.
  }
}
