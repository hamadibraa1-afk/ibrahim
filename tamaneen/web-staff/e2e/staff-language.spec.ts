import { type APIRequestContext, expect, test } from '@playwright/test';
import { API, CRISIS, KHALID, moodCrisis, onboardAndBook, signUpPatient } from './helpers/patient-api';
import { STAFF_PASSWORD, signIn, staffEmail } from './helpers/staff';
import { freshTotp } from './helpers/totp';

/**
 * Language switch on the sign-in page and the signed-in portal. The test patient books with Khalid (therapist, on
 * call), so his dashboard and console both show the crisis; the lead closes the event through the API afterwards.
 */
test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

test('the sign-in page switches to English, left-to-right', async ({ page }) => {
  await page.goto('/login');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { name: 'دخول الفريق' })).toBeVisible();
  await page.getByTestId('language-toggle').click();
  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByRole('heading', { name: 'Staff sign-in' })).toBeVisible();
  await expect(page.getByLabel('Work email')).toBeVisible();
  await expect(page.getByLabel('Password')).toBeVisible();
  await expect(page.getByTestId('continue')).toHaveText('Continue');
});

/** Cleanup: the lead closes the test's event through the worker API so it does not linger in other lists. */
async function closeAsLead(request: APIRequestContext, pseudonymId: string): Promise<void> {
  const login = await request.post(`${API}/v1/auth/login`, { data: { contact: staffEmail('lead'), password: STAFF_PASSWORD, client: 'staff' } });
  const { mfaToken } = (await login.json()) as { mfaToken: string };
  const mfa = await request.post(`${API}/v1/auth/login/mfa`, { data: { mfaToken, method: 'totp', code: await freshTotp('lead'), rememberDevice: false } });
  const { tokens } = (await mfa.json()) as { tokens: { accessToken: string } };
  const headers = { Authorization: `Bearer ${tokens.accessToken}` };
  const open = (await (await request.get(`${CRISIS}/v1/crisis/events/open`, { headers })).json()) as { eventId: string; pseudonymId: string }[];
  const event = open.find((e) => e.pseudonymId === pseudonymId);
  expect(event, 'the test event is still open').toBeTruthy();
  const closed = await request.post(`${CRISIS}/v1/crisis/events/${event!.eventId}/close`, {
    headers,
    data: { outcome: 'false_alarm', reason: 'e2e language test cleanup' },
  });
  expect(closed.status(), await closed.text()).toBeLessThan(300);
}

test('the dashboard and the console switch to English, with server texts in English too', async ({ page, request }) => {
  test.setTimeout(120_000);
  const patient = await signUpPatient(request);
  await onboardAndBook(request, patient, KHALID);
  const pid = (JSON.parse(Buffer.from(patient.accessToken.split('.')[1], 'base64url').toString()) as { pid: string }).pid;
  await moodCrisis(request, patient);

  await page.goto('/login');
  await signIn(page, 'khalid');
  await expect(page).toHaveURL(/\/dashboard/);
  const html = page.locator('html');
  const sidenav = page.getByTestId('sidenav');
  const content = page.locator('mat-sidenav-content');

  // Arabic first: right-to-left, the menu on the right, Arabic server texts.
  await expect(html).toHaveAttribute('dir', 'rtl');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^(صباح|مساء) الخير، /);
  const alert = page.getByTestId('crisis-alert').filter({ hasText: pid });
  await expect(alert.getByRole('heading')).toHaveText(`خطر حرج · ${pid}`);
  const nav = (await sidenav.boundingBox())!;
  const main = (await content.boundingBox())!;
  expect(nav.x).toBeGreaterThan(main.x);

  await page.getByTestId('language-toggle').click();
  await expect(html).toHaveAttribute('lang', 'en');
  await expect(html).toHaveAttribute('dir', 'ltr');
  await expect(page.getByTestId('shell')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^Good (morning|afternoon|evening), /);
  await expect(page.getByRole('heading', { name: 'New patients awaiting file approval' })).toBeVisible();
  await expect(page.getByTestId('sign-out')).toHaveText('Sign out');
  await expect(page.getByRole('link', { name: 'My dashboard' })).toBeVisible();
  await expect(page.getByTestId('language-toggle')).toContainText('العربية');
  // Server-provided texts follow the switch.
  await expect(alert.getByRole('heading')).toHaveText(`Critical risk · ${pid}`);
  await expect(alert).toHaveAttribute('dir', 'ltr');
  await expect(alert.getByTestId('acknowledge')).toHaveText('Acknowledge alert');
  // Material sidenav moves to the left, and the content (whose margin follows after a short transition) to its right.
  await expect
    .poll(async () => {
      const n = (await sidenav.boundingBox())!;
      const m = (await content.boundingBox())!;
      return n.x < m.x && n.x + n.width <= m.x + 1;
    })
    .toBe(true);

  // The console shows the level and source labels in English.
  await sidenav.getByRole('link', { name: 'Crisis console' }).click();
  await expect(page.getByRole('heading', { level: 1, name: 'Crisis console' })).toBeVisible();
  const event = page.getByTestId('event').filter({ hasText: pid });
  await expect(event).toContainText('Critical');
  await expect(event).toContainText('Mood check-in');
  await expect(event).toContainText('Not acknowledged');
  await event.click();
  await expect(page.getByTestId('timeline')).toContainText(/mood/i);

  // And back to Arabic without a reload.
  await page.getByTestId('language-toggle').click();
  await expect(html).toHaveAttribute('dir', 'rtl');
  await expect(event).toContainText('حرج');
  await expect(event).toContainText('تسجيل المزاج');

  await closeAsLead(request, pid);
});
