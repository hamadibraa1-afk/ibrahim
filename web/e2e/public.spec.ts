import { expect, test } from '@playwright/test';
import { ACCOUNTS, requireApi, signIn } from './helpers';

/** The customer journey, end to end: scan a site code, rate, then file a complaint. */
test.describe('customer pages', () => {
  test('rating and complaint work from a real site code', async ({ page, browser }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.supervisor);

    // Take a real QR token from the locations API.
    const token = await page.evaluate(async () => {
      const session = JSON.parse(localStorage.getItem('session') ?? '{}');
      const response = await fetch('/api/locations', { headers: { Authorization: `Bearer ${session.token}` } });
      const locations = await response.json();
      return locations[0]?.qrToken as string;
    });
    expect(token).toBeTruthy();

    // A customer arrives with a clean browser, as they would in real life.
    const customer = await browser.newContext();
    const visit = await customer.newPage();

    await visit.goto(`/r/${token}`);
    await visit.getByRole('button', { name: '★' }).nth(3).click();
    await visit.getByRole('button', { name: 'إرسال التقييم' }).click();
    await expect(visit.getByText('شكرًا لتقييمك')).toBeVisible();

    await visit.getByRole('link', { name: 'تقديم اقتراح' }).click();
    await visit.getByLabel('الاسم').fill('موزة الشامسي');
    await visit.getByLabel('رقم الهاتف').fill('0559876543');
    await visit.getByLabel('تفاصيل الاقتراح').fill('أقترح توفير لافتة إرشادية قرب المدخل.');
    await visit.getByRole('button', { name: 'إرسال' }).click();
    await expect(visit.getByText(/SCI-\d{4}-\d{5}/)).toBeVisible();

    await customer.close();

    // The supervisor must now see it in their inbox.
    await page.goto('/admin/feedback');
    await page.getByRole('button', { name: 'الاقتراحات' }).click();
    await expect(page.getByRole('cell', { name: 'موزة الشامسي' }).first()).toBeVisible();
  });

  test('the same device cannot rate the same site twice in a row', async ({ page, browser }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.supervisor);
    const token = await page.evaluate(async () => {
      const session = JSON.parse(localStorage.getItem('session') ?? '{}');
      const response = await fetch('/api/locations', { headers: { Authorization: `Bearer ${session.token}` } });
      return (await response.json())[1]?.qrToken as string;
    });

    const customer = await browser.newContext();
    const visit = await customer.newPage();
    for (const attempt of [1, 2]) {
      await visit.goto(`/r/${token}`);
      if (attempt === 1) {
        await visit.getByRole('button', { name: '★' }).nth(4).click();
        await visit.getByRole('button', { name: 'إرسال التقييم' }).click();
      }
      await expect(visit.getByText(/شكرًا/)).toBeVisible();
    }
    await customer.close();
  });
});
