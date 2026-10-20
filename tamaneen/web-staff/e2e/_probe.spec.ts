import { test } from '@playwright/test';
import { API, CRISIS } from './helpers/patient-api';
import { freshTotp } from './helpers/totp';
test('probe', async ({ request }) => {
  const login = await request.post(`${API}/v1/auth/login`, { data: { contact: 'mona@staff.tamaneen.dev', password: 'Tamaneen-Dev-2026!', client: 'staff' } });
  const { mfaToken } = await login.json();
  const mfa = await request.post(`${API}/v1/auth/login/mfa`, { data: { mfaToken, method: 'totp', code: await freshTotp('mona'), rememberDevice: false } });
  const t = await mfa.json(); console.log('mfa', mfa.status(), JSON.stringify({...t, tokens: {...t.tokens, accessToken: 'x', refreshToken: t.tokens?.refreshToken?.slice(0,5)}}), mfa.headers()['set-cookie']);
  const r = await request.post(`${API}/v1/auth/refresh`, { data: {} });
  const rt = await r.text(); console.log('refresh', r.status(), rt.replace(/ey[\w.-]+/g, 'JWT'));
});
