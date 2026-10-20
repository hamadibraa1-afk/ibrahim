import { expect, test } from '@playwright/test';

// Every portal page needs a signed-in staff member, so without a session the portal opens on the sign-in page.
// The language toggle and direction are checked there; the API is not needed (the silent refresh just fails).
test.describe('staff sign-in language and direction', () => {
  test.use({ locale: 'ar-SA' });

  test('opens on the sign-in page in Arabic, right-to-left', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/login/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.getByTestId('login')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { name: 'دخول الفريق' })).toBeVisible();
    await expect(page.getByLabel('البريد المهني')).toBeVisible();
    await expect(page.getByTestId('language-toggle')).toContainText('English');
  });

  test('the toggle switches the sign-in page to English and LTR, and the choice survives a reload', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('language-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByTestId('login')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('heading', { name: 'Staff sign-in' })).toBeVisible();
    await expect(page.getByLabel('Work email')).toBeVisible();
    await expect(page.getByTestId('language-toggle')).toContainText('العربية');

    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('heading', { name: 'Staff sign-in' })).toBeVisible();

    await page.getByTestId('language-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { name: 'دخول الفريق' })).toBeVisible();
  });

  test('validation messages follow the language', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('continue').click();
    await expect(page.getByText('مطلوب').first()).toBeVisible();
    await page.getByTestId('language-toggle').click();
    await expect(page.getByText('Required').first()).toBeVisible();
  });

  test('a deep link is kept for after sign-in', async ({ page }) => {
    await page.goto('/crisis');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fcrisis/);
  });
});
