import { type Page, expect, test } from '@playwright/test';
import { PATIENT_PHONE, type TestPatient, moodCrisis, onboardAndBookWithSara, signUpPatient } from './helpers/patient-api';
import { signIn } from './helpers/staff';
import { freshTotp } from './helpers/totp';

/**
 * S01 + S04 end to end against the running API and crisis worker: a new patient books with Sara, Sara reviews
 * and approves the file, the patient's mood check-in opens a level-4 crisis, Sara takes it in the console, reveals
 * the contact with a step-up code and adds a note; then Fahad (supervisor) closes it.
 */
test.describe.configure({ mode: 'serial' });
test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

let page: Page;
let patient: TestPatient;
let pseudonymId: string;
const note = `اتصلت بالمريض وهو بأمان الآن ${Date.now().toString(36)}`;

function pseudonymOf(p: TestPatient): string {
  return (JSON.parse(Buffer.from(p.accessToken.split('.')[1], 'base64url').toString()) as { pid: string }).pid;
}

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage();
});
test.afterAll(async () => {
  await page.close();
});

test('a new patient signs up and books a first session with Sara (API)', async ({ request }) => {
  patient = await signUpPatient(request);
  await onboardAndBookWithSara(request, patient);
  pseudonymId = pseudonymOf(patient);
  expect(pseudonymId).toMatch(/^P-[A-Z0-9]{4}-[A-Z0-9]{4}$/);
});

test('Sara signs in with password and authenticator code and sees her dashboard in Arabic', async () => {
  await page.goto('/');
  await expect(page).toHaveURL(/\/login/);
  await signIn(page, 'sara');
  await expect(page).toHaveURL(/\/dashboard/);
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(/^(صباح|مساء) الخير، أ\. سارة م\.$/);
  await expect(page.getByTestId('display-name')).toHaveText('أ. سارة م.');

  const row = page.getByTestId('pending-table').getByRole('row').filter({ hasText: pseudonymId });
  await expect(row).toHaveCount(1);
  await expect(row.getByTestId('risk-tag')).toHaveText('1 · روتيني');
});

test('Sara opens the file page (S02), sees the scores, and approves it', async () => {
  const row = page.getByTestId('pending-table').getByRole('row').filter({ hasText: pseudonymId });
  await row.getByTestId('review-file').click();
  await expect(page).toHaveURL(new RegExp(`/patients/${pseudonymId}$`));
  await expect(page.getByTestId('file-pseudonym')).toHaveText(pseudonymId);
  // Direct booking: the safety check stands in for the intake, so there are no questionnaire scores.
  await expect(page.getByText('حجز مباشر: فحص السلامة بدل الاستقبال.')).toBeVisible();
  await expect(page.getByTestId('score-PHQ-9')).toHaveCount(0);
  await expect(page.getByTestId('score-cssrs')).toContainText('سلبي');
  await page.getByTestId('approve').click();
  await expect(page.getByTestId('approved')).toBeVisible();

  // The approval is stored: back on the dashboard the file is no longer pending, also after a reload.
  await page.goto('/dashboard');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await expect(page.getByTestId('pending-table').getByRole('row').filter({ hasText: pseudonymId })).toHaveCount(0);
});

test('the patient’s mood note opens a crisis and the high-risk banner appears on the dashboard', async ({ request }) => {
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  await moodCrisis(request, patient);
  const banner = page.getByTestId('crisis-alert').filter({ hasText: pseudonymId });
  // Pushed live over SignalR; no reload.
  await expect(banner).toBeVisible({ timeout: 15_000 });
  await expect(banner).toHaveAttribute('dir', 'rtl');
  await expect(banner.getByRole('heading')).toHaveText(`خطر حرج · ${pseudonymId}`);
  await expect(banner.getByTestId('acknowledge')).toBeVisible();
});

test('the crisis console lists the event with its number, source and SLA countdown', async () => {
  // Sara is not on call (no console in her menu); she opens it from her patient's alert.
  await expect(page.getByRole('link', { name: 'غرفة الطوارئ', exact: true })).toHaveCount(0);
  await page.getByTestId('crisis-alert').filter({ hasText: pseudonymId }).getByTestId('open-console').click();
  await expect(page).toHaveURL(/\/crisis/);
  const event = page.getByTestId('event').filter({ hasText: pseudonymId });
  await expect(event).toBeVisible();
  await expect(event).toContainText(/CE-\d{4,}/);
  await expect(event).toContainText('حرج');
  await expect(event).toContainText('تسجيل المزاج');
  await expect(event).toContainText('لم يُقَرّ');
  const countdown = event.getByTestId('event-countdown');
  await expect(countdown).toHaveText(/^0[0-2]:\d\d$/);
  const first = await countdown.textContent();
  await expect(countdown).not.toHaveText(first!, { timeout: 3_000 });

  await event.click();
  await expect(event).toHaveAttribute('aria-current', 'true');
  const number = /CE-\d+/.exec((await event.textContent())!)![0];
  await expect(page.getByRole('heading', { level: 2, name: new RegExp(number) })).toBeVisible();
  await expect(page.getByTestId('sla')).toHaveText(/^0[0-2]:\d\d$/);
  await expect(page.getByTestId('masked-contact')).toContainText('••21');
  await expect(page.getByTestId('timeline')).toContainText('تسجيل المزاج');
});

test('Sara acknowledges and takes the case', async () => {
  await page.getByTestId('take').click();
  await expect(page.getByText('توليت الحالة.')).toBeVisible();
  await expect(page.getByTestId('take')).toHaveCount(0);
  await expect(page.getByText('أقرّ به أ. سارة م.')).toBeVisible();
  const event = page.getByTestId('event').filter({ hasText: pseudonymId });
  await expect(event).toContainText('تم الإقرار');
  await expect(event).toContainText('أ. سارة م.');
});

test('revealing the contact needs a fresh authenticator code; the phone appears as a tel: link', async () => {
  test.setTimeout(90_000);
  await page.getByTestId('reveal-contact').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByTestId('step-up-reason').fill('الاتصال بالمريض للاطمئنان عليه');
  // The sign-in code's step was consumed; wait for the next one.
  await dialog.getByTestId('step-up-code').fill(await freshTotp('sara'));
  await dialog.getByTestId('step-up-submit').click();
  await expect(dialog).toBeHidden();
  const revealed = page.getByTestId('revealed-contact');
  const phone = revealed.getByRole('link', { name: PATIENT_PHONE });
  await expect(phone).toHaveAttribute('href', `tel:${PATIENT_PHONE}`);
  await expect(revealed).toContainText('أختي نورة');
  await expect(revealed.getByRole('link', { name: '0559876543' })).toHaveAttribute('href', 'tel:0559876543');
  await expect(page.getByTestId('call')).toHaveAttribute('href', `tel:${PATIENT_PHONE}`);
  await expect(page.getByTestId('contact-hides')).toContainText(/\d\d:\d\d/);
});

test('Sara adds a note and the timeline shows it', async () => {
  await page.getByTestId('add-note').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByTestId('note-text').fill(note);
  await dialog.getByTestId('note-submit').click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText('أُضيفت الملاحظة.')).toBeVisible();
  await expect(page.getByTestId('timeline')).toContainText(note);
  await expect(page.getByTestId('timeline').locator('li').filter({ hasText: note })).toContainText('أ. سارة م.');
});

test('Sara signs out', async () => {
  await page.getByTestId('sign-out').click();
  await expect(page).toHaveURL(/\/login/);
  await expect(page.getByRole('heading', { name: 'دخول الفريق' })).toBeVisible();
  // The refresh cookie is gone: a reload does not restore the session.
  await page.goto('/crisis');
  await expect(page).toHaveURL(/\/login\?returnUrl=%2Fcrisis/);
});

test('Fahad (supervisor) closes the case with an outcome and a reason; it leaves the list', async () => {
  await signIn(page, 'fahad');
  await expect(page).toHaveURL(/\/crisis/);
  const event = page.getByTestId('event').filter({ hasText: pseudonymId });
  await event.click();
  await expect(event).toHaveAttribute('aria-current', 'true');
  await expect(page.getByTestId('timeline')).toContainText(note);
  await page.getByTestId('close-case').click();
  const dialog = page.getByRole('dialog');
  await dialog.getByTestId('close-submit').click();
  await expect(dialog.getByTestId('outcome-error')).toBeVisible();
  await dialog.getByTestId('close-safe_contacted').click();
  await dialog.getByTestId('close-reason').fill('تواصلت الأخصائية مع المريض وهو بأمان، ووُضعت خطة سلامة.');
  await dialog.getByTestId('close-submit').click();
  await expect(dialog).toBeHidden();
  await expect(event).toHaveCount(0);
  await page.reload();
  await expect(page.getByRole('heading', { level: 1, name: 'غرفة الطوارئ' })).toBeVisible();
  await expect(page.getByTestId('event').first().or(page.getByTestId('no-events'))).toBeVisible();
  await expect(page.getByTestId('event').filter({ hasText: pseudonymId })).toHaveCount(0);
});
