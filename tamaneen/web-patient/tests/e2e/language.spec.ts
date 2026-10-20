import { expect, test } from '@playwright/test';

test.describe('language and direction', () => {
  test.use({ locale: 'ar-SA' });

  test('Arabic is the default and renders right-to-left', async ({ browser }) => {
    // A browser that asks for neither Arabic nor English gets Arabic, the platform default.
    const context = await browser.newContext({ locale: 'fr-FR', extraHTTPHeaders: { 'Accept-Language': 'fr-FR,fr;q=0.9' } });
    const page = await context.newPage();
    await page.goto('/');
    await expect(page).toHaveURL(/\/ar$/);
    await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('نحن هنا لنسمعك، بلا أحكام');
    await expect(page.getByTestId('help-now')).toHaveText('مساعدة فورية');
    await context.close();
  });

  test('an English browser lands on the English page', async ({ browser }) => {
    const context = await browser.newContext({ locale: 'en-US', extraHTTPHeaders: { 'Accept-Language': 'en-US,en;q=0.9' } });
    const page = await context.newPage();
    await page.goto('/');
    await expect(page).toHaveURL(/\/en$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText("We're here to listen, without judgment");
    await context.close();
  });

  test('the toggle switches content and direction instantly and the choice is remembered', async ({ page }) => {
    await page.goto('/ar');
    const toggle = page.getByTestId('language-toggle');
    await expect(toggle).toHaveText('English');
    await toggle.click();

    await expect(page).toHaveURL(/\/en$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText("We're here to listen, without judgment");
    await expect(page.getByTestId('help-now')).toHaveText('Help now');
    await expect(toggle).toHaveText('العربية');

    // The middleware stored the choice: the bare URL now opens in English.
    await page.goto('/');
    await expect(page).toHaveURL(/\/en$/);

    await page.getByTestId('language-toggle').click();
    await expect(page).toHaveURL(/\/ar$/);
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
  });

  test('the layout mirrors: brand at the start edge, Help now at the end edge', async ({ page }) => {
    await page.goto('/ar');
    const brand = await page.getByTestId('brand').boundingBox();
    const help = await page.getByTestId('help-now').boundingBox();
    expect(brand!.x).toBeGreaterThan(help!.x);

    await page.goto('/en');
    const brandEn = await page.getByTestId('brand').boundingBox();
    const helpEn = await page.getByTestId('help-now').boundingBox();
    expect(brandEn!.x).toBeLessThan(helpEn!.x);
  });

  test('the emergency line shows the configured number as a call link in both languages', async ({ page }) => {
    await page.goto('/ar');
    await expect(page.locator('a[href="tel:911"]')).toContainText('911');
    await page.goto('/en');
    await expect(page.locator('a[href="tel:911"]')).toContainText('Call 911');
  });

  test('unknown pages show a localized not-found page with Help now still visible', async ({ page }) => {
    await page.goto('/en/no-such-page');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Page not found');
    await expect(page.getByTestId('help-now')).toBeVisible();
    await page.goto('/ar/no-such-page');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('الصفحة غير موجودة');
  });

  test('the locale middleware never redirects to another site', async ({ request, baseURL }) => {
    for (const path of ['//evil.example/', '/%2F%2Fevil.example', '/ar//evil.example', '/\\evil.example']) {
      // Absolute URL so the client sends the raw path instead of treating "//host" as protocol-relative.
      const response = await request.get(`${baseURL}${path}`, { maxRedirects: 0, failOnStatusCode: false });
      const location = response.headers()['location'] ?? '';
      expect(location, path).not.toMatch(/^(https?:)?\/\/(?!localhost)/i);
    }
  });

  test('touch targets are at least 44px', async ({ page }) => {
    await page.goto('/ar');
    for (const id of ['language-toggle', 'help-now', 'cta-start']) {
      const box = await page.getByTestId(id).boundingBox();
      expect(box!.height, id).toBeGreaterThanOrEqual(44);
    }
  });
});

test.describe('BFF to API', () => {
  test.skip(!process.env.E2E_API_URL, 'Set E2E_API_URL to a running Tamaneen.Api to run these.');

  test('the API answers in the language chosen in the app', async ({ page, request }) => {
    await page.goto('/ar');
    await page.getByTestId('language-toggle').click();
    await expect(page).toHaveURL(/\/en$/);
    const english = await page.evaluate(async () => (await fetch('/api/v1/meta/locale')).json());
    expect(english).toMatchObject({ language: 'en', dir: 'ltr', message: 'Welcome to Tamaneen.' });

    await page.getByTestId('language-toggle').click();
    await expect(page).toHaveURL(/\/ar$/);
    const arabic = await page.evaluate(async () => (await fetch('/api/v1/meta/locale')).json());
    expect(arabic).toMatchObject({ language: 'ar', dir: 'rtl', message: 'مرحبًا بك في طمأنينة.' });

    const problem = await request.get('/api/v1/me/language', { headers: { 'Accept-Language': 'en' } });
    expect(problem.status()).toBe(405);
  });

  test('path traversal through the BFF is refused', async ({ request }) => {
    const response = await request.get('/api/v1/..%2F..%2Fhealth/ready');
    expect(response.status()).toBe(404);
  });
});
