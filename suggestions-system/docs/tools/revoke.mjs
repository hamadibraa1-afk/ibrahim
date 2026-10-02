import { chromium } from 'playwright';
const BASE = process.env.BASE_URL ?? 'http://localhost:4200', out = process.argv[2];
const b = await chromium.launch({ ...(process.env.CHROMIUM ? { executablePath: process.env.CHROMIUM } : {}) });
async function login(code) {
  const p = await (await b.newContext({ viewport: { width: 1440, height: 900 } })).newPage();
  await p.goto(`${BASE}/login`);
  await p.fill('input[formcontrolname=userCode]', code); await p.fill('input[formcontrolname=password]', 'Passw0rd!');
  await p.click('button[type=submit]'); await p.waitForURL(u => !u.toString().includes('/login'));
  return p;
}
const emp = await login('EMP-2001');
await emp.goto(`${BASE}/my-proposals`); await emp.waitForTimeout(1500);
console.log('employee page before:', emp.url().replace(BASE, ''));
const admin = await login('EMP-1001');
const id = await emp.evaluate(() => JSON.parse(localStorage.getItem('sci.proposals.user')).id);
// the admin suspends the employee from the user-management screen's API (same call the button makes)
const t0 = Date.now();
await admin.evaluate(async id => {
  const csrf = document.cookie.split('; ').find(c => c.startsWith('XSRF-TOKEN=')).split('=')[1];
  await fetch(`/api/users/${id}/suspend`, { method: 'POST', headers: { 'X-XSRF-TOKEN': csrf, 'Content-Type': 'application/json' }, body: '{}' });
}, id);
await emp.waitForURL(u => u.toString().includes('/login'), { timeout: 10000 });
console.log(`employee tab signed out automatically after ${Date.now() - t0} ms ->`, emp.url().replace(BASE, ''));
await emp.waitForTimeout(500);
await emp.screenshot({ path: `${out}/16-revoked-signed-out.png` });
await admin.evaluate(async id => {
  const csrf = document.cookie.split('; ').find(c => c.startsWith('XSRF-TOKEN=')).split('=')[1];
  await fetch(`/api/users/${id}/activate`, { method: 'POST', headers: { 'X-XSRF-TOKEN': csrf, 'Content-Type': 'application/json' }, body: '{}' });
}, id);
console.log('employee re-activated');
await b.close();
