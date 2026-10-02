import { chromium } from 'playwright';
const BASE = process.env.BASE_URL ?? 'http://localhost:4200';
const out = process.argv[2];
const browser = await chromium.launch({ ...(process.env.CHROMIUM ? { executablePath: process.env.CHROMIUM } : {}) });
const errors = [];
async function session(code) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 1 });
  const page = await ctx.newPage();
  page.on('pageerror', e => errors.push(`${code}: ${e.message}`));
  if (code) {
    await page.goto(`${BASE}/login`);
    await page.fill('input[formcontrolname=userCode]', code);
    await page.fill('input[formcontrolname=password]', 'Passw0rd!');
    await page.click('button[type=submit]');
    await page.waitForURL(u => !u.toString().includes('/login'));
  }
  return page;
}
async function shot(page, path, name, full = false) {
  await page.goto(`${BASE}${path}`);
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(900);
  await page.screenshot({ path: `${out}/${name}.png`, fullPage: full });
}
const anon = await session(null);
await shot(anon, '/login', '01-login');

const admin = await session('EMP-1001');
await shot(admin, '/dashboard', '02-dashboard', true);
await shot(admin, '/impact', '03-impact-roi', true);
await shot(admin, '/proposals/1', '04-proposal-impact', true);
await shot(admin, '/proposals/7', '05-proposal-escalated', true);
await shot(admin, '/users', '06-users');
await admin.click('text=+ إضافة مستخدم'); await admin.waitForTimeout(500);
await admin.screenshot({ path: `${out}/07-user-modal.png` });
await shot(admin, '/form-settings', '08-form-settings', true);
await shot(admin, '/audit', '09-audit');

const screener = await session('EMP-1002');
await shot(screener, '/screening', '10-screening-blind');
await shot(screener, '/proposals/8', '11-screening-detail-blind', true);
await shot(screener, '/notifications', '12-notifications');

const manager = await session('EMP-1004');
await shot(manager, '/my-proposals', '13-manager-rerouted', true);

const employee = await session('EMP-2001');
await shot(employee, '/my-proposals', '14-employee-progress', true);
await shot(employee, '/submit', '15-submit-form', true);
console.log('errors:', errors.length ? errors : 'none');
await browser.close();
