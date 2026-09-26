import { expect, test } from '@playwright/test';
import { ACCOUNTS, hoursNow, requireApi, signIn } from './helpers';

/**
 * Mobile project: the browser reports a location, so check-in can be exercised
 * end to end. Geolocation is set to City Centre Sharjah in the config.
 */
test.describe('collector app', () => {
  test.beforeEach(async ({ page }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.collector);
  });

  test('today screen shows the assigned site', async ({ page }) => {
    await expect(page).toHaveURL(/\/me$/);
    await expect(page.getByText(/سيتي سنتر الشارقة|لا توجد لديك مناوبة اليوم/)).toBeVisible();
  });

  test('check-in succeeds inside the geofence', async ({ page }) => {
    test.skip(hoursNow() < 8 || hoursNow() >= 16, 'The morning shift is not running right now.');
    const button = page.getByRole('button', { name: 'تسجيل الحضور' });
    test.skip(await button.count() === 0, 'Already checked in for this shift.');

    await button.click();
    await expect(page.getByText('سجّلت حضورك الساعة')).toBeVisible({ timeout: 15_000 });
  });

  test('outside the geofence the app offers an exception request', async ({ page, context }) => {
    test.skip(hoursNow() < 8 || hoursNow() >= 16, 'The morning shift is not running right now.');
    const button = page.getByRole('button', { name: 'تسجيل الحضور' });
    test.skip(await button.count() === 0, 'Already checked in for this shift.');

    await context.setGeolocation({ latitude: 24.4539, longitude: 54.3773 }); // Abu Dhabi
    await button.click();
    await expect(page.getByText(/خارج نطاق الموقع/)).toBeVisible({ timeout: 15_000 });
    await expect(page.getByRole('button', { name: 'طلب حضور استثنائي' })).toBeVisible();
  });

  test('a leave request reaches the supervisor inbox', async ({ page }) => {
    await page.getByRole('link', { name: 'طلباتي' }).click();
    await page.getByLabel('النوع').selectOption('Leave');
    await page.getByLabel('السبب').fill('اختبار آلي');
    await page.getByRole('button', { name: 'إرسال' }).click();
    await expect(page.getByText('بانتظار القرار').first()).toBeVisible();
  });
});
