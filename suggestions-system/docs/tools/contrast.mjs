import { chromium } from 'playwright';
import { PNG } from 'pngjs';
const BASE = process.env.BASE_URL ?? 'http://localhost:4200';
const browser = await chromium.launch({ ...(process.env.CHROMIUM ? { executablePath: process.env.CHROMIUM } : {}) });

const lin = c => { c /= 255; return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4; };
const L = ([r, g, b]) => 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
const ratio = (a, b) => { const [x, y] = [L(a), L(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };

async function login(code) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  if (code) {
    await page.goto(`${BASE}/login`);
    await page.fill('input[formcontrolname=userCode]', code);
    await page.fill('input[formcontrolname=password]', 'Passw0rd!');
    await page.click('button[type=submit]');
    await page.waitForURL(u => !u.toString().includes('/login'));
  }
  return page;
}

async function check(page, label) {
  // every visible element that directly holds text
  const items = await page.evaluate(() => {
    const out = [];
    for (const el of document.querySelectorAll('body *')) {
      if (!['SCRIPT', 'STYLE', 'svg', 'path'].includes(el.tagName) && [...el.childNodes].some(n => n.nodeType === 3 && n.textContent.trim())) {
        const r = el.getBoundingClientRect();
        if (r.width < 4 || r.height < 4 || r.bottom < 0 || r.top > innerHeight || r.right < 0 || r.left > innerWidth) continue;
        if (el.closest('[disabled]') || el.closest('[aria-hidden=true]')) continue;   // WCAG exempts inactive controls
        let op = 1; for (let e = el; e; e = e.parentElement) op *= +getComputedStyle(e).opacity;
        if (op < 0.95) continue;
        const cs = getComputedStyle(el);
        const m = cs.color.match(/[\d.]+/g).map(Number);
        const size = parseFloat(cs.fontSize), bold = +cs.fontWeight >= 700;
        out.push({ text: [...el.childNodes].filter(n => n.nodeType === 3).map(n => n.textContent.trim()).join(' ').slice(0, 40),
          color: m.slice(0, 3), alpha: m[3] ?? 1, large: size >= 24 || (bold && size >= 18.66),
          x: Math.max(0, r.left), y: Math.max(0, r.top), w: Math.min(r.width, innerWidth - Math.max(0, r.left)), h: Math.min(r.height, innerHeight - Math.max(0, r.top)) });
      }
    }
    return out;
  });
  const hide = await page.addStyleTag({ content: '*,*::placeholder{color:transparent!important;-webkit-text-fill-color:transparent!important;text-shadow:none!important}' });
  await page.waitForTimeout(150);
  const png = PNG.sync.read(await page.screenshot());
  await hide.evaluate(n => n.remove());

  const fails = []; let min = 99;
  for (const it of items) {
    const lum = [];
    for (let yy = Math.floor(it.y); yy < Math.min(png.height, it.y + it.h); yy += 2)
      for (let xx = Math.floor(it.x); xx < Math.min(png.width, it.x + it.w); xx += 2) {
        const i = (yy * png.width + xx) * 4; lum.push([png.data[i], png.data[i + 1], png.data[i + 2]]);
      }
    if (!lum.length) continue;
    // worst case for the reader: the background pixel closest in lightness to the text
    const sorted = lum.map(p => [L(p), p]).sort((a, b) => a[0] - b[0]);
    const median = sorted[Math.floor(sorted.length / 2)][1];
    const fg = it.color.map((c, k) => c * it.alpha + median[k] * (1 - it.alpha));
    const r = ratio(fg, median);
    min = Math.min(min, r);
    const need = it.large ? 3 : 4.5;
    if (r < need) fails.push(`${r.toFixed(2)} < ${need}  «${it.text}»`);
  }
  console.log(`${label.padEnd(28)} texts=${String(items.length).padStart(3)}  min=${min.toFixed(2)}  failures=${fails.length}`);
  fails.forEach(f => console.log('      ' + f));
  return fails.length;
}

const pages = [
  [null, '/login'], ['EMP-1001', '/dashboard'], ['EMP-1001', '/impact'], ['EMP-1001', '/proposals/1'],
  ['EMP-1001', '/proposals/7'], ['EMP-1001', '/users'], ['EMP-1001', '/form-settings'], ['EMP-1001', '/audit'],
  ['EMP-1002', '/screening'], ['EMP-1002', '/proposals/8'], ['EMP-1002', '/notifications'],
  ['EMP-1004', '/my-proposals'], ['EMP-2001', '/my-proposals'], ['EMP-2001', '/submit'],
];
let total = 0; const sessions = {};
for (const [who, path] of pages) {
  const page = sessions[who] ??= await login(who);
  await page.goto(`${BASE}${path}`); await page.waitForLoadState('networkidle'); await page.waitForTimeout(700);
  total += await check(page, `${who ?? 'anonymous'} ${path}`);
}
console.log(`TOTAL failures: ${total}`);
await browser.close();
