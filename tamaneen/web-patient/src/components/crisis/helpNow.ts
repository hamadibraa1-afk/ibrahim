'use client';

import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';
import type { HelpResponse } from '@/lib/api/types';

/**
 * The one request the patient app queues while offline (EC-04): "Help now" (PB-26). A press stores a request id
 * in localStorage before anything is sent, so the request survives a dead connection, a closed tab or a slow API.
 * The same id is used for every retry; the API treats it as an idempotency key, so a double tap or a resend
 * after a lost response never opens a second crisis event. Only the id and a time are stored, never content.
 */
export const HELP_QUEUE_KEY = 'tamaneen.helpNow.pending';
/** Fired on window when a queued request has been delivered, so every Help now control can move on. */
export const HELP_SENT_EVENT = 'tamaneen:help-sent';
/** While a request is pending, try again this often even if the browser never reports going back online. */
export const HELP_RETRY_MS = 15_000;

export interface PendingHelp {
  requestId: string;
  queuedAt: string;
}

export type HelpResult = { status: 'sent'; response: HelpResponse } | { status: 'queued' } | { status: 'failed'; error: unknown };

function newId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  // RFC 4122 v4 from getRandomValues, for older browsers.
  const b = crypto.getRandomValues(new Uint8Array(16));
  b[6] = (b[6]! & 0x0f) | 0x40;
  b[8] = (b[8]! & 0x3f) | 0x80;
  const h = [...b].map((x) => x.toString(16).padStart(2, '0')).join('');
  return `${h.slice(0, 8)}-${h.slice(8, 12)}-${h.slice(12, 16)}-${h.slice(16, 20)}-${h.slice(20)}`;
}

export function pendingHelp(): PendingHelp | null {
  try {
    const raw = window.localStorage.getItem(HELP_QUEUE_KEY);
    if (!raw) return null;
    const value = JSON.parse(raw) as Partial<PendingHelp>;
    return typeof value.requestId === 'string' && value.requestId ? { requestId: value.requestId, queuedAt: String(value.queuedAt ?? '') } : null;
  } catch {
    return null;
  }
}

/** The pending request, or a new one stored now. A second press while one is pending reuses its id. */
function enqueue(): PendingHelp {
  const existing = pendingHelp();
  if (existing) return existing;
  const created = { requestId: newId(), queuedAt: new Date().toISOString() };
  try {
    window.localStorage.setItem(HELP_QUEUE_KEY, JSON.stringify(created));
  } catch {
    // Storage blocked (private mode): the request is still sent now, it just cannot wait for the connection.
  }
  return created;
}

function dequeue(requestId: string) {
  try {
    if (pendingHelp()?.requestId === requestId) window.localStorage.removeItem(HELP_QUEUE_KEY);
  } catch {
    // Nothing to clean up.
  }
}

/** Errors worth retrying later: no connection, the API or the crisis gate briefly unavailable, or a sign-in to redo. */
function retryable(error: unknown): boolean {
  if (!(error instanceof ApiError)) return true;
  return error.status === 401 || error.status === 408 || error.status === 429 || error.status >= 500;
}

let inFlight: Promise<HelpResult> | null = null;

/** Sends the pending request, if any. Concurrent calls share one HTTP request. */
export function flushHelp(): Promise<HelpResult | null> {
  const pending = pendingHelp();
  if (!pending) return Promise.resolve(null);
  return send(pending);
}

function send(pending: PendingHelp): Promise<HelpResult> {
  inFlight ??= api<HelpResponse>('crisis/help', { method: 'POST', body: { requestId: pending.requestId } })
    .then((response): HelpResult => {
      dequeue(pending.requestId);
      window.dispatchEvent(new CustomEvent(HELP_SENT_EVENT, { detail: response }));
      return { status: 'sent', response };
    })
    .catch((error: unknown): HelpResult => {
      if (retryable(error)) return { status: 'queued' };
      dequeue(pending.requestId);
      return { status: 'failed', error };
    })
    .finally(() => {
      inFlight = null;
    });
  return inFlight;
}

/** One press of Help now: queue first, then send. Resolves 'queued' when it has to wait for the connection. */
export function requestHelp(): Promise<HelpResult> {
  const pending = enqueue();
  if (typeof navigator !== 'undefined' && navigator.onLine === false) return Promise.resolve({ status: 'queued' });
  return send(pending);
}

/** For tests. */
export function resetHelpQueue() {
  inFlight = null;
  try {
    window.localStorage.removeItem(HELP_QUEUE_KEY);
  } catch {
    // ignore
  }
}
