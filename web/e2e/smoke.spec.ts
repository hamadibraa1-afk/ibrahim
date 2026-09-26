import { expect, test } from '@playwright/test';
import { mockJson } from './helpers';

/**
 * Runs with no backend: every API call is mocked. It protects the parts that break
 * most often in a front end — routing by role, right-to-left layout, dialog behaviour
 * and the public pages a customer sees.
 */
test.describe('smoke (no backend)', () => {
  const session = (role: string) => ({
    token: 'test-token', expiresAt: new Date(Date.now() + 3_600_000).toISOString(),
    userId: '11111111-1111-1111-1111-111111111111', fullName: 'مستخدم الاختبار', role, language: 'ar',
  });

  test('sign-in page is Arabic, right to left, and validates before enabling submit', async ({ page }) => {
    await page.goto('/login');

    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { name: 'جمعية الشارقة الخيرية' })).toBeVisible();

    const submit = page.getByRole('button', { name: 'دخول' });
    await expect(submit).toBeDisabled();

    await page.getByLabel('الرقم الوظيفي').fill('1001');
    await page.getByLabel('كلمة المرور').fill('Test@1234');
    await expect(submit).toBeEnabled();
  });

  test('language toggle switches direction and wording', async ({ page }) => {
    await page.goto('/login');
    await page.getByRole('button', { name: 'English' }).click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible();
  });

  test('wrong credentials show the translated message, not a raw error', async ({ page }) => {
    await mockJson(page, '**/api/auth/login', { code: 'auth.invalid_credentials', message: 'x' }, 401);
    await page.goto('/login');
    await page.getByLabel('الرقم الوظيفي').fill('9999');
    await page.getByLabel('كلمة المرور').fill('wrong-password');
    await page.getByRole('button', { name: 'دخول' }).click();
    await expect(page.getByText('الرقم الوظيفي أو كلمة المرور غير صحيحة')).toBeVisible();
  });

  test('a user with both modules lands on the picker', async ({ page }) => {
    // Playwright tries the most recently registered route first, so the catch-all goes in before the login mock.
    await mockJson(page, '**/api/**', []);
    await mockJson(page, '**/api/auth/login', session('SystemAdmin'));
    await page.goto('/login');
    await page.getByLabel('الرقم الوظيفي').fill('1001');
    await page.getByLabel('كلمة المرور').fill('Test@1234');
    await page.getByRole('button', { name: 'دخول' }).click();

    await expect(page).toHaveURL(/\/select/);
    await expect(page.getByRole('heading', { name: 'الموظفون الإداريون' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'المحصّلون وفرق العمل الميداني' })).toBeVisible();
  });

  test('an HR-only user skips the picker and goes straight to HR', async ({ page }) => {
    // Playwright tries the most recently registered route first, so the catch-all goes in before the login mock.
    await mockJson(page, '**/api/**', []);
    await mockJson(page, '**/api/auth/login', session('HrManager'));
    await page.goto('/login');
    await page.getByLabel('الرقم الوظيفي').fill('1005');
    await page.getByLabel('كلمة المرور').fill('Test@1234');
    await page.getByRole('button', { name: 'دخول' }).click();

    await expect(page).toHaveURL(/\/hr$/);
    await expect(page.getByRole('link', { name: 'ملفات الموظفين' })).toBeVisible();
  });

  test('a customer sees the rating page with complaint and suggestion routes', async ({ page }) => {
    await mockJson(page, '**/api/public/r/**', { nameAr: 'سيتي سنتر الشارقة', nameEn: 'City Centre', chooseEmployee: [] });
    await page.goto('/r/11111111-1111-1111-1111-111111111111');

    await expect(page.getByRole('heading', { name: 'سيتي سنتر الشارقة' })).toBeVisible();
    await expect(page.getByText('رأيك يهمّنا')).toBeVisible();
    await expect(page.getByRole('button', { name: 'إرسال التقييم' })).toBeDisabled();

    await page.getByRole('link', { name: 'تسجيل شكوى' }).click();
    await expect(page).toHaveURL(/\/feedback\?kind=Complaint/);
    await expect(page.getByLabel('رقم الهاتف')).toBeVisible();
  });

  test('a complaint returns a reference number the customer can keep', async ({ page }) => {
    await mockJson(page, '**/api/public/r/*', { nameAr: 'سيتي سنتر الشارقة', nameEn: 'City Centre', chooseEmployee: [] });
    await mockJson(page, '**/api/public/r/*/feedback', { reference: 'SCI-2026-00007' });
    await page.goto('/r/11111111-1111-1111-1111-111111111111/feedback?kind=Complaint');

    await page.getByLabel('الاسم').fill('أحمد المري');
    await page.getByLabel('رقم الهاتف').fill('0501234567');
    await page.getByLabel('تفاصيل الشكوى').fill('انتظرت وقتًا طويلًا عند نقطة التحصيل.');
    await page.getByRole('button', { name: 'إرسال' }).click();

    await expect(page.getByText('SCI-2026-00007')).toBeVisible();
  });

  test('signing out clears the session and protected routes bounce to login', async ({ page }) => {
    // Playwright tries the most recently registered route first, so the catch-all goes in before the login mock.
    await mockJson(page, '**/api/**', []);
    await mockJson(page, '**/api/auth/login', session('HrManager'));
    await page.goto('/login');
    await page.getByLabel('الرقم الوظيفي').fill('1005');
    await page.getByLabel('كلمة المرور').fill('Test@1234');
    await page.getByRole('button', { name: 'دخول' }).click();
    await expect(page).toHaveURL(/\/hr$/);

    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await expect(page).toHaveURL(/\/login/);

    await page.goto('/hr/employees');
    await expect(page).toHaveURL(/\/login/);
  });
});
