import { type APIRequestContext, expect } from '@playwright/test';

export const API = 'http://localhost:5080';
export const CRISIS = 'http://localhost:5090';
export const PATIENT_PASSWORD = 'Quiet-Morning-Walk-77';
export const PATIENT_PHONE = '0551234521';

export interface TestPatient {
  email: string;
  nickname: string;
  accessToken: string;
}

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>, what: string): Promise<T> {
  expect(response.ok(), `${what}: ${response.status()} ${await response.text()}`).toBeTruthy();
  return (await response.json()) as T;
}

/** Signs up a fresh adult patient through the patient API and returns their bearer token. */
export async function signUpPatient(request: APIRequestContext): Promise<TestPatient> {
  const unique = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
  const email = `e2e-staff-${unique}@example.test`;
  const nickname = `اختبار ${unique.slice(-4)}`;
  const { eligibilityToken } = await json<{ eligibilityToken: string }>(
    await request.post(`${API}/v1/auth/eligibility`, { data: { isAdult: true, cityCode: 'RUH' } }),
    'eligibility',
  );
  const { challengeId } = await json<{ challengeId: string }>(
    await request.post(`${API}/v1/auth/signup`, { data: { eligibilityToken, nickname, contactType: 'email', contact: email, password: PATIENT_PASSWORD, language: 'ar' } }),
    'signup',
  );
  let code: string | undefined;
  for (let i = 0; i < 20 && !code; i++) {
    const outbox = await request.get(`${API}/dev/outbox`, { params: { to: email.toLowerCase() } });
    if (outbox.ok()) {
      const body = (await outbox.json()) as { text?: string } | { text?: string }[];
      const text = Array.isArray(body) ? body.map((m) => m.text ?? '').join('\n') : (body.text ?? '');
      code = /\b(\d{6})\b/.exec(text)?.[1];
    }
    if (!code) await new Promise((r) => setTimeout(r, 250));
  }
  expect(code, 'verification code in the dev outbox').toBeTruthy();
  const verified = await json<{ tokens: { accessToken: string } }>(
    await request.post(`${API}/v1/auth/signup/verify`, { data: { challengeId, code } }),
    'signup/verify',
  );
  return { email, nickname, accessToken: verified.tokens.accessToken };
}

function auth(patient: TestPatient) {
  return { Authorization: `Bearer ${patient.accessToken}` };
}

/** Consents, safety info, direct path, a negative C-SSRS and a confirmed booking with Sara: the patient becomes hers. */
export async function onboardAndBookWithSara(request: APIRequestContext, patient: TestPatient): Promise<{ therapistId: string }> {
  const headers = auth(patient);

  const put = async (path: string, data: unknown) => {
    const r = await request.put(`${API}${path}`, { headers, data });
    expect(r.ok(), `${path}: ${r.status()} ${await r.text()}`).toBeTruthy();
  };
  const post = async <T>(path: string, data: unknown): Promise<T> => {
    const r = await request.post(`${API}${path}`, { headers, data });
    expect(r.ok(), `${path}: ${r.status()} ${await r.text()}`).toBeTruthy();
    const text = await r.text();
    return (text ? JSON.parse(text) : undefined) as T;
  };
  await put('/v1/me/consents', { items: [{ key: 'platform', granted: true }, { key: 'ai', granted: true }, { key: 'share_with_therapist', granted: true }] });
  await put('/v1/me/safety-info', { contactPhone: PATIENT_PHONE, trustedName: 'أختي نورة', trustedPhone: '0559876543', shareLocation: true });
  await put('/v1/me/path', { path: 'direct' });
  await post('/v1/screening/cssrs', { answers: [false, false, false, false, false, false] });

  const therapistsBody = await json<unknown>(await request.get(`${API}/v1/therapists`, { headers, params: { specialty: 'sleep' } }), 'therapists');
  const therapists = (Array.isArray(therapistsBody) ? therapistsBody : ((therapistsBody as { items?: unknown[] }).items ?? [])) as { id?: string; therapistId?: string; displayName: string }[];
  const sara = therapists.find((t) => t.displayName === 'أ. سارة م.');
  expect(sara, `Sara in ${JSON.stringify(therapists.map((t) => t.displayName))}`).toBeTruthy();
  const therapistId = (sara!.id ?? sara!.therapistId)!;

  const slotsBody = await json<unknown>(await request.get(`${API}/v1/therapists/${therapistId}/slots`, { headers }), 'slots');
  const slots = (Array.isArray(slotsBody) ? slotsBody : ((slotsBody as { slots?: unknown[]; items?: unknown[] }).slots ?? (slotsBody as { items?: unknown[] }).items ?? [])) as { id?: string; slotId?: string }[];
  expect(slots.length, 'Sara has free slots').toBeGreaterThan(0);
  // Spread parallel runs over different slots.
  const slot = slots[Math.floor(Math.random() * Math.min(slots.length, 20))];
  const hold = await post<{ holdId?: string; id?: string }>('/v1/bookings/holds', { slotId: slot.id ?? slot.slotId });
  await post(`/v1/bookings/holds/${hold.holdId ?? hold.id}/confirm`, {});
  return { therapistId };
}

/** A mood check-in with a crisis note: the gate opens a level-4 event. */
export async function moodCrisis(request: APIRequestContext, patient: TestPatient): Promise<void> {
  const r = await request.post(`${API}/v1/mood`, { headers: auth(patient), data: { score: 1, tags: [], note: 'ما عاد أتحمل، أبي أموت الليلة' } });
  const text = await r.text();
  expect(r.ok(), `mood: ${r.status()} ${text}`).toBeTruthy();
  expect(JSON.parse(text).crisis, `mood response ${text}`).toBe(true);
}
