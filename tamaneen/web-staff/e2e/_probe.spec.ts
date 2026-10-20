import { test } from '@playwright/test';
import { API, signUpPatient, onboardAndBookWithSara } from './helpers/patient-api';
import { freshTotp } from './helpers/totp';
test('probe', async ({ request }) => {
  test.setTimeout(90_000);
  const p = await signUpPatient(request);
  await onboardAndBookWithSara(request, p);
  const pid = JSON.parse(Buffer.from(p.accessToken.split('.')[1], 'base64url').toString()).pid;
  const login = await request.post(`${API}/v1/auth/login`, { data: { contact: 'mona@staff.tamaneen.dev', password: 'Tamaneen-Dev-2026!', client: 'staff' } });
  const { mfaToken } = await login.json();
  // mona isn't Sara; use sara
  const l2 = await request.post(`${API}/v1/auth/login`, { data: { contact: 'sara@staff.tamaneen.dev', password: 'Tamaneen-Dev-2026!', client: 'staff' } });
  const m2 = (await l2.json()).mfaToken;
  const mfa = await request.post(`${API}/v1/auth/login/mfa`, { data: { mfaToken: m2, method: 'totp', code: await freshTotp('sara'), rememberDevice: false } });
  if (!mfa.ok()) { console.log('mfa', mfa.status(), await mfa.text()); return; }
  const h = { Authorization: `Bearer ${(await mfa.json()).tokens.accessToken}` };
  const a = await request.post(`${API}/v1/staff/patients/${pid}/intake/approve`, { headers: h, data: {} });
  console.log('approve', a.status(), await a.text());
  const i = await request.get(`${API}/v1/staff/patients/${pid}/intake`, { headers: h }); console.log('intake', (await i.text()));
  const d = await request.get(`${API}/v1/staff/dashboard`, { headers: h });
  const dj = await d.json(); console.log('still pending?', dj.pendingProfiles.some((x: any) => x.pseudonymId === pid), dj.stats.pendingProfiles, dj.pendingProfiles.length);
});
