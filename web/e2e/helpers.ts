import { expect, Page, Route } from '@playwright/test';

export const ACCOUNTS = {
  admin: { number: '1001', password: 'Test@1234' },
  supervisor: { number: '1002', password: 'Test@1234' },
  viewer: { number: '1003', password: 'Test@1234' },
  hrManager: { number: '1005', password: 'Test@1234' },
  collector: { number: '2001', password: 'Test@1234' },
} as const;

/** Signs in through the real login form and waits for the app to settle. */
export async function signIn(page: Page, account: { number: string; password: string }): Promise<void> {
  await page.goto('/login');
  await page.getByLabel('الرقم الوظيفي').fill(account.number);
  await page.getByLabel('كلمة المرور').fill(account.password);
  await page.getByRole('button', { name: 'دخول' }).click();
  await expect(page).not.toHaveURL(/\/login/, { timeout: 15_000 });
}

/** Skips the test with a clear reason when the API is not running. */
export async function requireApi(page: Page): Promise<void> {
  const base = process.env['E2E_API_URL'] ?? 'http://localhost:5000';
  const response = await page.request.get(`${base}/health`).catch(() => null);
  if (!response?.ok())
    throw new Error(`API is not reachable at ${base}. Start it with: dotnet run -- --reset=true`);
}

/** Mocks one API path with a JSON payload; used by the backend-free smoke suite. */
export async function mockJson(page: Page, pattern: string | RegExp, body: unknown, status = 200): Promise<void> {
  await page.route(pattern, (route: Route) =>
    route.fulfill({ status, contentType: 'application/json', body: JSON.stringify(body) }));
}

export const hoursNow = (): number => Number(
  new Intl.DateTimeFormat('en-GB', { hour: '2-digit', hour12: false, timeZone: 'Asia/Dubai' }).format(new Date()));
