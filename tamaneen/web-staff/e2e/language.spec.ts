import { expect, test } from '@playwright/test';

test.describe('staff portal language and direction', () => {
  test.use({ locale: 'ar-SA' });

  test('opens in Arabic, right-to-left, with the side menu on the right', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.getByTestId('shell')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('link', { name: 'مرضاي' })).toBeVisible();
    const nav = await page.getByTestId('sidenav').boundingBox();
    expect(nav!.x).toBeGreaterThan(700);
  });

  test('the toggle switches everything to English and LTR, and the choice survives a reload', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('language-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByTestId('shell')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('link', { name: 'My patients' })).toBeVisible();
    await expect(page.getByTestId('language-toggle')).toContainText('العربية');
    const nav = await page.getByTestId('sidenav').boundingBox();
    expect(nav!.x).toBeLessThan(10);

    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('link', { name: 'My patients' })).toBeVisible();

    await page.getByTestId('language-toggle').click();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  });

  test('switching role changes the menu, in the current language', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('role-switch').click();
    await page.getByRole('option', { name: 'مدير النظام' }).click();
    await expect(page.getByRole('link', { name: 'الإضافات' })).toBeVisible();
    await expect(page.getByRole('link', { name: 'مرضاي' })).toHaveCount(0);
    await page.getByTestId('language-toggle').click();
    await expect(page.getByRole('link', { name: 'Add-ons' })).toBeVisible();
  });

  test('the role picker overlay opens in the current direction', async ({ page }) => {
    await page.goto('/');
    await page.getByTestId('role-switch').click();
    await expect(page.locator('.cdk-overlay-pane [dir="rtl"], .cdk-overlay-pane .mat-mdc-select-panel').first()).toBeVisible();
    const panelDir = await page.locator('.cdk-overlay-pane').first().evaluate((el) => getComputedStyle(el).direction);
    expect(panelDir).toBe('rtl');
  });
});

test.describe('API in the chosen language', () => {
  test.skip(!process.env.E2E_API_URL, 'Set E2E_API_URL to a running Tamaneen.Api to run these.');
  test.use({ locale: 'ar-SA' });

  test('the server message follows the toggle', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByTestId('api-message')).toHaveText('مرحبًا بك في طمأنينة.');
    await page.getByTestId('language-toggle').click();
    await expect(page.getByTestId('api-message')).toHaveText('Welcome to Tamaneen.');
  });
});
