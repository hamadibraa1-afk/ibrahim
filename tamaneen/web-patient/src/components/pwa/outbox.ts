'use client';

import { useSyncExternalStore } from 'react';
import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';

/**
 * The patient's outbox (P66, EC-01, EC-07, PB-50): writes that must survive a weak or missing connection are
 * stored on the device before anything is sent, then sent in the order they were made: now if possible,
 * otherwise when the connection returns and on a timer (0, 2, 10, 30 s, then every 30 s). Each write carries
 * an Idempotency-Key that never changes across retries, so the API saves it once and answers a resend with the
 * first result. Free text still reaches the crisis gate, when it arrives at the server.
 *
 * Items belong to the patient who wrote them (`owner`, their pseudonym): only the signed-in owner's items are
 * listed or sent, so a different patient on the same device never sees or sends someone else's queue. Sign-out
 * and account deletion clear the whole outbox. Nothing is ever dropped silently: an item the API refuses for
 * good (4xx) stays, marked as not sent with the API's message, until the patient discards it.
 *
 * Storage: IndexedDB, each item encrypted with AES-GCM under a non-extractable device key kept in the same
 * database (the P66 rule: the local box is encrypted). If IndexedDB is unavailable (some private windows), the
 * queue falls back to localStorage in plain form, because losing the patient's words would be worse; that
 * storage is cleared on sign-out like the rest. Help now keeps its own, smaller queue (crisis/helpNow.ts).
 */

export type OutboxKind = 'mood' | 'homework-checkin' | 'safety-plan';

export interface OutboxItem {
  /** Also the Idempotency-Key. */
  id: string;
  /** Pseudonym of the patient who wrote it. */
  owner: string;
  kind: OutboxKind;
  method: 'POST' | 'PUT';
  /** API path under /v1, e.g. "mood" or "homework/{id}/checkins". */
  path: string;
  body: unknown;
  /** A short name shown in the list, e.g. the homework title. */
  label?: string;
  createdAt: string;
  attempts: number;
  /** Epoch ms of the next automatic attempt. */
  nextAttemptAt: number;
  state: 'waiting' | 'failed';
  /** The API's localized reason when the write was refused for good. */
  error?: string;
}

export interface OutboxStore {
  load(): Promise<OutboxItem[]>;
  put(item: OutboxItem): Promise<void>;
  remove(id: string): Promise<void>;
  clear(): Promise<void>;
}

export type WriteResult<T> = { status: 'sent'; response: T } | { status: 'queued'; item: OutboxItem } | { status: 'rejected'; error: unknown };

/** Fired on window when a queued item is delivered in the background: detail is { item, response }. */
export const OUTBOX_SENT_EVENT = 'tamaneen:outbox-sent';
export const OUTBOX_LOCAL_KEY = 'tamaneen.outbox.v1';
const DB_NAME = 'tamaneen-outbox';
const ITEMS = 'items';
const KEYS = 'keys';
const KEY_ID = 'outbox';
/** P66: retry after 0, 2, 10, 30 seconds, then every 30 seconds. */
export const BACKOFF_MS = [0, 2_000, 10_000, 30_000];

export interface OutboxSentDetail {
  item: OutboxItem;
  response: unknown;
}

// ---------- storage ----------

function request<T>(req: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    req.onsuccess = () => resolve(req.result);
    req.onerror = () => reject(req.error);
  });
}

function openDb(): Promise<IDBDatabase> {
  const req = indexedDB.open(DB_NAME, 1);
  req.onupgradeneeded = () => {
    req.result.createObjectStore(ITEMS, { keyPath: 'id' });
    req.result.createObjectStore(KEYS);
  };
  return request(req);
}

interface SealedItem {
  id: string;
  owner: string;
  iv: Uint8Array;
  data: ArrayBuffer;
}

async function withDb<T>(fn: (db: IDBDatabase) => Promise<T>): Promise<T> {
  const db = await openDb();
  try {
    return await fn(db);
  } finally {
    db.close();
  }
}

async function deviceKey(db: IDBDatabase): Promise<CryptoKey> {
  const existing = (await request(db.transaction(KEYS, 'readonly').objectStore(KEYS).get(KEY_ID))) as CryptoKey | undefined;
  if (existing) return existing;
  const key = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  await request(db.transaction(KEYS, 'readwrite').objectStore(KEYS).put(key, KEY_ID));
  return key;
}

const indexedDbStore: OutboxStore = {
  load: () =>
    withDb(async (db) => {
      const key = await deviceKey(db);
      const sealed = (await request(db.transaction(ITEMS, 'readonly').objectStore(ITEMS).getAll())) as SealedItem[];
      const out: OutboxItem[] = [];
      for (const s of sealed) {
        try {
          const clear = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: s.iv }, key, s.data);
          out.push(JSON.parse(new TextDecoder().decode(clear)) as OutboxItem);
        } catch {
          // Unreadable (key replaced): it can never be sent; leave it for sign-out to remove.
        }
      }
      return out;
    }),
  put: (item) =>
    withDb(async (db) => {
      const key = await deviceKey(db);
      const iv = crypto.getRandomValues(new Uint8Array(12));
      const data = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, new TextEncoder().encode(JSON.stringify(item)));
      await request(db.transaction(ITEMS, 'readwrite').objectStore(ITEMS).put({ id: item.id, owner: item.owner, iv, data } satisfies SealedItem));
    }),
  remove: (id) =>
    withDb(async (db) => {
      await request(db.transaction(ITEMS, 'readwrite').objectStore(ITEMS).delete(id));
    }),
  clear: async () => {
    await request(indexedDB.deleteDatabase(DB_NAME)).catch(() => undefined);
  },
};

function readLocal(): OutboxItem[] {
  try {
    const raw = window.localStorage.getItem(OUTBOX_LOCAL_KEY);
    const parsed = raw ? (JSON.parse(raw) as unknown) : [];
    return Array.isArray(parsed) ? (parsed as OutboxItem[]).filter((i) => typeof i?.id === 'string' && typeof i.owner === 'string') : [];
  } catch {
    return [];
  }
}

function writeLocal(list: OutboxItem[]) {
  if (list.length === 0) window.localStorage.removeItem(OUTBOX_LOCAL_KEY);
  else window.localStorage.setItem(OUTBOX_LOCAL_KEY, JSON.stringify(list));
}

/** The fallback when IndexedDB is missing. Throws when storage is blocked; the item then lives in memory only. */
export const localStorageStore: OutboxStore = {
  load: async () => readLocal(),
  put: async (item) => writeLocal([...readLocal().filter((i) => i.id !== item.id), item]),
  remove: async (id) => writeLocal(readLocal().filter((i) => i.id !== id)),
  clear: async () => window.localStorage.removeItem(OUTBOX_LOCAL_KEY),
};

let override: OutboxStore | null = null;
let chosen: OutboxStore | null = null;

/** For tests: replace the storage (null goes back to the browser's). Also forgets what was loaded. */
export function setOutboxStore(next: OutboxStore | null) {
  override = next;
  chosen = null;
  resetState();
}

function idbUsable(): boolean {
  return typeof indexedDB !== 'undefined' && typeof crypto !== 'undefined' && Boolean(crypto.subtle);
}

async function storage(): Promise<OutboxStore> {
  if (override) return override;
  if (chosen) return chosen;
  if (idbUsable()) {
    try {
      await indexedDbStore.load();
      chosen = indexedDbStore;
      return chosen;
    } catch {
      // Blocked (private window, quota): fall through.
    }
  }
  chosen = localStorageStore;
  return chosen;
}

// ---------- state ----------

let items: OutboxItem[] = [];
let loading: Promise<void> | null = null;
let owner: string | null = null;
let snapshot: OutboxItem[] = [];
const EMPTY: OutboxItem[] = [];
const listeners = new Set<() => void>();
/** Outcomes of writes the patient is waiting for on screen (submitWrite), by item id. */
const outcomes = new Map<string, { sent: true; response: unknown } | { sent: false; error: unknown }>();
let flushing: Promise<void> = Promise.resolve();

function resetState() {
  items = [];
  loading = null;
  outcomes.clear();
  flushing = Promise.resolve();
  publish();
}

function publish() {
  snapshot = owner ? items.filter((i) => i.owner === owner).sort((a, b) => a.createdAt.localeCompare(b.createdAt)) : EMPTY;
  listeners.forEach((l) => l());
}

function ensureLoaded(): Promise<void> {
  loading ??= storage()
    .then((s) => s.load())
    .then((stored) => {
      const known = new Set(items.map((i) => i.id));
      items = [...items, ...stored.filter((i) => !known.has(i.id))];
      publish();
    })
    .catch(() => undefined);
  return loading;
}

async function save(item: OutboxItem) {
  // Replace in place: an update must not move an item behind newer ones.
  items = items.some((i) => i.id === item.id) ? items.map((i) => (i.id === item.id ? item : i)) : [...items, item];
  publish();
  try {
    await (await storage()).put(item);
  } catch {
    // Storage blocked or full: the item still waits in memory for this visit.
  }
}

async function drop(id: string) {
  items = items.filter((i) => i.id !== id);
  publish();
  try {
    await (await storage()).remove(id);
  } catch {
    // Nothing stored.
  }
}

/** The signed-in patient whose items are listed and sent (null: nobody, nothing is sent). */
export function setOutboxOwner(next: string | null) {
  if (owner === next) return;
  owner = next;
  publish();
  if (next) void ensureLoaded();
}

export function outboxOwner(): string | null {
  return owner;
}

/** The signed-in patient's items, oldest first. */
export function outboxItems(): OutboxItem[] {
  return snapshot;
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

/** The signed-in patient's items as React state (empty on the server). */
export function useOutbox(): OutboxItem[] {
  return useSyncExternalStore(
    subscribe,
    () => snapshot,
    () => EMPTY,
  );
}

/** When the next automatic attempt is due, or null when nothing is waiting. */
export function nextAttemptAt(): number | null {
  const waiting = snapshot.filter((i) => i.state === 'waiting');
  return waiting.length ? Math.min(...waiting.map((i) => i.nextAttemptAt)) : null;
}

// ---------- sending ----------

function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6]! & 0x0f) | 0x40;
  b[8] = (b[8]! & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

/** Worth trying again later: no connection, a sign-in to redo, the API or the crisis gate briefly unavailable, or the first copy still running. */
export function retryable(error: unknown): boolean {
  if (!(error instanceof ApiError)) return true;
  if (error.code === 'error.idempotency.in_progress') return true;
  return error.status === 401 || error.status === 408 || error.status === 429 || error.status >= 500;
}

function refusal(error: unknown): string | undefined {
  return error instanceof ApiError ? (error.problem.detail ?? error.problem.title) : undefined;
}

function backoff(attempts: number): number {
  return BACKOFF_MS[Math.min(attempts, BACKOFF_MS.length - 1)]!;
}

function offline(): boolean {
  return typeof navigator !== 'undefined' && navigator.onLine === false;
}

async function runFlush(force: boolean, interactiveId?: string) {
  await ensureLoaded();
  const who = owner;
  if (!who || offline()) return;
  const now = Date.now();
  // A write the patient just made goes out behind everything older, so all of those are tried now too.
  const all = force || interactiveId !== undefined;
  const waiting = snapshot.filter((i) => i.state === 'waiting');
  for (const item of waiting) {
    // In order: nothing overtakes an older item that is still waiting for its next attempt.
    if (!all && item.nextAttemptAt > now) break;
    // Signed out (or another patient signed in) mid-way: stop; their items are not ours to send.
    if (owner !== who || !items.some((i) => i.id === item.id)) break;
    try {
      const response = await api<unknown>(item.path, { method: item.method, body: item.body, idempotencyKey: item.id, navigate: false });
      await drop(item.id);
      if (item.id === interactiveId) outcomes.set(item.id, { sent: true, response });
      else if (typeof window !== 'undefined') window.dispatchEvent(new CustomEvent<OutboxSentDetail>(OUTBOX_SENT_EVENT, { detail: { item, response } }));
    } catch (error) {
      if (!items.some((i) => i.id === item.id)) continue;
      if (retryable(error)) {
        const attempts = item.attempts + 1;
        await save({ ...item, attempts, nextAttemptAt: Date.now() + backoff(attempts) });
        // Keep the order: a later write (a newer safety plan) must not overtake an earlier one.
        break;
      }
      if (item.id === interactiveId) {
        // The patient is on the form: the refusal is shown there and the entry stays on screen to correct.
        outcomes.set(item.id, { sent: false, error });
        await drop(item.id);
      } else {
        await save({ ...item, state: 'failed', error: refusal(error) });
      }
    }
  }
}

/** Sends what is due, oldest first. `force` sends every waiting item now (the ⟳ button, the connection is back). */
export function flushOutbox(options: { force?: boolean } = {}): Promise<void> {
  flushing = flushing.then(() => runFlush(options.force ?? false)).catch(() => undefined);
  return flushing;
}

/**
 * Saves a write in the outbox, then tries to send it (behind anything older that is still waiting).
 * 'sent' carries the API response, 'queued' means it waits on the device, 'rejected' means the API refused it
 * (shown on the form; nothing is kept). Without a signed-in owner the write is simply sent once.
 */
export async function submitWrite<T>(input: { kind: OutboxKind; method: 'POST' | 'PUT'; path: string; body: unknown; label?: string }): Promise<WriteResult<T>> {
  const who = owner;
  const id = newId();
  if (!who) {
    try {
      return { status: 'sent', response: await api<T>(input.path, { method: input.method, body: input.body, idempotencyKey: id }) };
    } catch (error) {
      return { status: 'rejected', error };
    }
  }
  await ensureLoaded();
  const item: OutboxItem = { ...input, id, owner: who, createdAt: new Date().toISOString(), attempts: 0, nextAttemptAt: Date.now(), state: 'waiting' };
  await save(item);
  flushing = flushing.then(() => runFlush(false, id)).catch(() => undefined);
  await flushing;
  const outcome = outcomes.get(id);
  outcomes.delete(id);
  if (outcome?.sent) return { status: 'sent', response: outcome.response as T };
  if (outcome) return { status: 'rejected', error: outcome.error };
  return { status: 'queued', item: items.find((i) => i.id === id) ?? item };
}

/** The patient discards an item (P66: only ever by their choice). */
export function discardOutboxItem(id: string): Promise<void> {
  return drop(id);
}

/** Tries a refused item again, as new (same key, so it is still saved once). */
export async function retryOutboxItem(id: string): Promise<void> {
  const item = items.find((i) => i.id === id);
  if (!item) return;
  await save({ ...item, state: 'waiting', error: undefined, attempts: 0, nextAttemptAt: Date.now() });
  await flushOutbox({ force: true });
}

/** Sign-out and account deletion: removes every item of every patient from this device. Never throws. */
export async function clearOutbox(): Promise<void> {
  items = [];
  outcomes.clear();
  loading = Promise.resolve();
  publish();
  await Promise.all(
    [override, idbUsable() ? indexedDbStore : null, localStorageStore].map(async (s) => {
      try {
        await s?.clear();
      } catch {
        // Nothing stored there.
      }
    }),
  );
}

/** For tests. */
export function resetOutbox() {
  owner = null;
  resetState();
}
