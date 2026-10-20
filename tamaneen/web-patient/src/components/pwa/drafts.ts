/**
 * Unsent text kept on this device for a short time (P35: "what you typed is saved on this device for
 * 10 minutes"), so a session expiry or a lost connection never throws away what the patient wrote.
 * Every access is guarded: storage can be blocked, full, or missing in private windows.
 */
const PREFIX = 'tamaneen.draft.';
export const DRAFT_TTL_MS = 10 * 60 * 1000;

interface StoredDraft {
  text: string;
  savedAt: number;
}

export function saveDraft(key: string, text: string, now = Date.now()): void {
  try {
    if (text.trim() === '') window.localStorage.removeItem(PREFIX + key);
    else window.localStorage.setItem(PREFIX + key, JSON.stringify({ text, savedAt: now } satisfies StoredDraft));
  } catch {
    // Storage unavailable: the draft only lives in memory.
  }
}

export function loadDraft(key: string, now = Date.now()): string {
  try {
    const raw = window.localStorage.getItem(PREFIX + key);
    if (!raw) return '';
    const draft = JSON.parse(raw) as Partial<StoredDraft>;
    if (typeof draft.text !== 'string' || typeof draft.savedAt !== 'number' || now - draft.savedAt > DRAFT_TTL_MS) {
      window.localStorage.removeItem(PREFIX + key);
      return '';
    }
    return draft.text;
  } catch {
    return '';
  }
}

export function clearDraft(key: string): void {
  try {
    window.localStorage.removeItem(PREFIX + key);
  } catch {
    // Nothing to clear.
  }
}
