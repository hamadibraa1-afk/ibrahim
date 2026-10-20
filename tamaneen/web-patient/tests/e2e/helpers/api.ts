import { expect, type Page } from '@playwright/test';
import { msUntilNextStep, totp } from './totp';

export const API_URL = process.env.E2E_API_URL ?? 'http://localhost:5080';
export const WORKER_URL = process.env.E2E_WORKER_URL ?? 'http://localhost:5090';
export const STAFF_PASSWORD = 'Tamaneen-Dev-2026!';
export const SUPERVISOR = 'fahad@staff.tamaneen.dev';

/** A strong password that is not on any breach list. */
export const STRONG_PASSWORD = 'Quiet-Harbor-Lantern-2026!';

export function uniqueEmail(tag: string): string {
  return `e2e-${tag}-${Date.now()}-${Math.random().toString(36).slice(2, 7)}@example.com`;
}

async function call<T>(base: string, method: string, path: string, body?: unknown, token?: string): Promise<{ status: number; data: T }> {
  const response = await fetch(new URL(path, base), {
    method,
    headers: {
      'Content-Type': 'application/json',
      'Accept-Language': 'en',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await response.text();
  let data: unknown = text;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    // Not JSON; keep the text.
  }
  return { status: response.status, data: data as T };
}

/** The 6-digit code from the last message the development outbox "sent" to this contact. */
export async function codeFromOutbox(contact: string): Promise<string> {
  let code: string | null = null;
  await expect
    .poll(
      async () => {
        const { status, data } = await call<{ text: string }>(API_URL, 'GET', `/dev/outbox?to=${encodeURIComponent(contact)}`);
        code = status === 200 ? (data.text.match(/\b\d{6}\b/)?.[0] ?? null) : null;
        return code;
      },
      { message: `a code in the outbox for ${contact}`, timeout: 10_000 },
    )
    .not.toBeNull();
  return code!;
}

const staffTokens = new Map<string, string>();

/**
 * Signs a staff account in (password, then TOTP) and caches the access token for this worker. A TOTP step
 * is accepted once per account, so a refused code waits for the next step and tries again.
 */
export async function staffToken(contact: string = SUPERVISOR): Promise<string> {
  const cached = staffTokens.get(contact);
  if (cached) return cached;
  for (let attempt = 0; attempt < 3; attempt++) {
    const login = await call<{ status: string; mfaToken: string }>(API_URL, 'POST', '/v1/auth/login', { contact, password: STAFF_PASSWORD, client: 'staff' });
    expect(login.status, JSON.stringify(login.data)).toBe(200);
    expect(login.data.status).toBe('mfa');
    const mfa = await call<{ tokens: { accessToken: string } }>(API_URL, 'POST', '/v1/auth/login/mfa', {
      mfaToken: login.data.mfaToken,
      method: 'totp',
      code: totp(),
      rememberDevice: false,
    });
    if (mfa.status === 200) {
      staffTokens.set(contact, mfa.data.tokens.accessToken);
      return mfa.data.tokens.accessToken;
    }
    await new Promise((resolve) => setTimeout(resolve, msUntilNextStep() + 500));
  }
  throw new Error(`Staff sign-in failed for ${contact}`);
}

export interface TriageCase { id: string; pseudonymId: string }

/** Supervisor review of a patient's triage case, which creates the three therapist suggestions. */
export async function reviewTriage(pseudonymId: string): Promise<void> {
  const token = await staffToken();
  let found: TriageCase | undefined;
  await expect
    .poll(
      async () => {
        const list = await call<TriageCase[]>(API_URL, 'GET', '/v1/staff/triage', undefined, token);
        expect(list.status).toBe(200);
        found = list.data.find((c) => c.pseudonymId === pseudonymId);
        return Boolean(found);
      },
      { message: `triage case for ${pseudonymId}`, timeout: 10_000 },
    )
    .toBe(true);
  const review = await call(API_URL, 'POST', `/v1/staff/triage/${found!.id}/review`, { riskLevel: 1, focus: ['anxiety', 'sleep', 'stress'] }, token);
  expect(review.status, JSON.stringify(review.data)).toBe(200);
}

export interface OpenCrisisEvent { eventId: string; pseudonymId: string; status: string; number: string }

/** Waits for the open crisis event of this patient (crisis worker). */
export async function openCrisisEvent(pseudonymId: string): Promise<OpenCrisisEvent> {
  const token = await staffToken();
  let found: OpenCrisisEvent | undefined;
  await expect
    .poll(
      async () => {
        const list = await call<OpenCrisisEvent[]>(WORKER_URL, 'GET', '/v1/crisis/events/open', undefined, token);
        expect(list.status).toBe(200);
        found = list.data.find((e) => e.pseudonymId === pseudonymId);
        return Boolean(found);
      },
      { message: `open crisis event for ${pseudonymId}`, timeout: 15_000 },
    )
    .toBe(true);
  return found!;
}

export async function closeCrisisEvent(eventId: string, reason: string): Promise<void> {
  const token = await staffToken();
  const closed = await call(WORKER_URL, 'POST', `/v1/crisis/events/${eventId}/close`, { outcome: 'safe_contacted', reason }, token);
  expect([200, 204], JSON.stringify(closed.data)).toContain(closed.status);
}

/** The signed-in patient's pseudonym, read through the app's own BFF with the page's cookies. */
export async function pseudonymOf(page: Page): Promise<string> {
  const me = await page.evaluate(async () => (await fetch('/api/v1/me')).json());
  expect(me.pseudonymId).toBeTruthy();
  return me.pseudonymId as string;
}
