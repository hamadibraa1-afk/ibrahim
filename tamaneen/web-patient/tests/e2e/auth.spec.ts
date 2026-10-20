import { expect, test, type Page } from '@playwright/test';
import { STRONG_PASSWORD, createPatient, uniqueEmail } from './helpers/api';
import { expectDirection } from './helpers/flows';

test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

async function signIn(page: Page, email: string, password: string) {
  await page.goto('/ar/login');
  await page.getByLabel('رقم الجوال أو البريد').fill(email);
  await page.getByLabel('كلمة المرور', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'تسجيل الدخول', exact: true }).click();
}

test.describe('sign-in, session and sign-out', () => {
  test.skip(!process.env.E2E_API_URL, 'Set E2E_API_URL to a running Tamaneen.Api.');

  test('a wrong password shows the remaining attempts in Arabic', async ({ page }) => {
    const email = uniqueEmail('login');
    await createPatient(email);

    await signIn(page, email, 'Wrong-Password-0000!');
    await expectDirection(page, 'ar');
    const problem = page.getByTestId('login-problem');
    await expect(problem).toBeVisible();
    await expect(problem).toHaveAttribute('role', 'alert');
    await expect(problem).toContainText('تبقّت 4 محاولات قبل الإيقاف المؤقت 15 دقيقة.');
    await expect(page).toHaveURL(/\/ar\/login$/);

    await page.getByRole('button', { name: 'تسجيل الدخول', exact: true }).click();
    await expect(problem).toContainText('تبقّت 3 محاولات قبل الإيقاف المؤقت 15 دقيقة.');

    // The right password still works before the lock.
    await page.getByLabel('كلمة المرور', { exact: true }).fill(STRONG_PASSWORD);
    await page.getByRole('button', { name: 'تسجيل الدخول', exact: true }).click();
    await expect(page).toHaveURL(/\/ar\/start\/consents$/);
  });

  test('clearing the cookies on a signed-in page shows the session-expired page', async ({ page, context }) => {
    const email = uniqueEmail('expired');
    await createPatient(email);
    await signIn(page, email, STRONG_PASSWORD);
    await expect(page).toHaveURL(/\/ar\/start\/consents$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('قبل أن نبدأ');

    await context.clearCookies();
    await page.reload();
    await expect(page).toHaveURL(/\/ar\/session-expired$/);
    await expectDirection(page, 'ar');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('انتهت الجلسة لحمايتك');
    await page.getByRole('link', { name: 'تسجيل الدخول' }).click();
    await expect(page).toHaveURL(/\/ar\/login$/);
  });

  test('signing out from settings returns to the start page', async ({ page }) => {
    const email = uniqueEmail('logout');
    await createPatient(email);
    await signIn(page, email, STRONG_PASSWORD);
    await expect(page).toHaveURL(/\/ar\/start\/consents$/);

    await page.goto('/ar/settings');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('الإعدادات');
    await page.getByRole('button', { name: 'تسجيل الخروج', exact: true }).click();
    await expect(page).toHaveURL(/\/ar$/);
    await expect(page.getByTestId('cta-start')).toBeVisible();

    // The session is really gone.
    await page.goto('/ar/settings');
    await expect(page).toHaveURL(/\/ar\/session-expired$/);
  });
});
