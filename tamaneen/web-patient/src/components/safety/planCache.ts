import type { SafetyPlan } from '@/lib/api/types';

/**
 * The copy of the safety plan kept on this device so P37 opens offline. The plan is health data, so it is never
 * stored as plain text (P37 rule: no PHI in local storage). It is encrypted with AES-GCM under a WebCrypto key
 * that is generated non-extractable and kept in IndexedDB: page script can use the key but cannot read its bytes,
 * and the ciphertext in localStorage is useless without it. If the browser offers no WebCrypto or IndexedDB
 * (an insecure origin, some private modes), nothing is cached and the plan simply loads from the network.
 * Sign-out removes both the ciphertext and the key.
 */
export const SAFETY_PLAN_KEY = 'tamaneen.safetyPlan.v2';
/** The plaintext cache of earlier versions; removed whenever the cache is touched. */
export const LEGACY_PLAN_KEY = 'tamaneen.safetyPlan.v1';
const DB_NAME = 'tamaneen-device-keys';
const STORE = 'keys';
const KEY_ID = 'safety-plan';

export interface CachedSafetyPlan {
  locale: string;
  savedAt: string;
  plan: SafetyPlan;
}

/** Where the device key lives. IndexedDB in the browser; tests pass an in-memory store. */
export interface PlanKeyStore {
  get(): Promise<CryptoKey | null>;
  put(key: CryptoKey): Promise<void>;
  clear(): Promise<void>;
}

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function openDb(): Promise<IDBDatabase> {
  const req = indexedDB.open(DB_NAME, 1);
  req.onupgradeneeded = () => req.result.createObjectStore(STORE);
  return request(req);
}

const indexedDbStore: PlanKeyStore = {
  async get() {
    const db = await openDb();
    try {
      return ((await request(db.transaction(STORE, 'readonly').objectStore(STORE).get(KEY_ID))) as CryptoKey | undefined) ?? null;
    } finally {
      db.close();
    }
  },
  async put(key) {
    const db = await openDb();
    try {
      await request(db.transaction(STORE, 'readwrite').objectStore(STORE).put(key, KEY_ID));
    } finally {
      db.close();
    }
  },
  async clear() {
    await request(indexedDB.deleteDatabase(DB_NAME)).catch(() => undefined);
  },
};

let store: PlanKeyStore | null = null;

/** For tests. */
export function setPlanKeyStore(next: PlanKeyStore | null) {
  store = next;
}

function keyStore(): PlanKeyStore | null {
  if (store) return store;
  if (typeof indexedDB === 'undefined' || typeof crypto === 'undefined' || !crypto.subtle) return null;
  return indexedDbStore;
}

async function deviceKey(create: boolean): Promise<CryptoKey | null> {
  const s = keyStore();
  if (!s) return null;
  const existing = await s.get();
  if (existing || !create) return existing;
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  await s.put(key);
  return key;
}

function toBase64(bytes: Uint8Array): string {
  let s = '';
  bytes.forEach((b) => (s += String.fromCharCode(b)));
  return btoa(s);
}

function fromBase64(text: string): Uint8Array {
  return Uint8Array.from(atob(text), (c) => c.charCodeAt(0));
}

function dropLegacy() {
  try {
    window.localStorage.removeItem(LEGACY_PLAN_KEY);
  } catch {
    // Nothing stored.
  }
}

function isPlan(value: unknown): value is SafetyPlan {
  return typeof value === 'object' && value !== null && typeof (value as SafetyPlan).sections === 'object' && (value as SafetyPlan).sections !== null;
}

/** The last plan fetched on this device. Null when absent, unreadable, or the key is gone. */
export async function readCachedPlan(): Promise<CachedSafetyPlan | null> {
  dropLegacy();
  try {
    const raw = window.localStorage.getItem(SAFETY_PLAN_KEY);
    if (!raw) return null;
    const key = await deviceKey(false);
    if (!key) return null;
    const { iv, data } = JSON.parse(raw) as { iv: string; data: string };
    const clear = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromBase64(iv) }, key, fromBase64(data));
    const parsed = JSON.parse(new TextDecoder().decode(clear)) as Partial<CachedSafetyPlan>;
    if (!isPlan(parsed.plan) || typeof parsed.savedAt !== 'string') return null;
    return { locale: String(parsed.locale ?? ''), savedAt: parsed.savedAt, plan: { ...parsed.plan, contacts: parsed.plan.contacts ?? [] } };
  } catch {
    return null;
  }
}

export async function writeCachedPlan(plan: SafetyPlan, locale: string, now = new Date()): Promise<void> {
  dropLegacy();
  try {
    const key = await deviceKey(true);
    if (!key) return;
    const iv = crypto.getRandomValues(new Uint8Array(12));
    const body = new TextEncoder().encode(JSON.stringify({ locale, savedAt: now.toISOString(), plan } satisfies CachedSafetyPlan));
    const data = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, body));
    window.localStorage.setItem(SAFETY_PLAN_KEY, JSON.stringify({ iv: toBase64(iv), data: toBase64(data) }));
  } catch {
    // Storage full or blocked: the plan still shows from the network.
  }
}

/** For sign-out: the plan must not stay on a shared device. Removes the ciphertext and destroys the key. */
export async function clearCachedPlan(): Promise<void> {
  dropLegacy();
  try {
    window.localStorage.removeItem(SAFETY_PLAN_KEY);
  } catch {
    // Nothing stored.
  }
  try {
    await keyStore()?.clear();
  } catch {
    // No key to remove.
  }
}
