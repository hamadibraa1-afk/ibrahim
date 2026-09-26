import { expect, test } from '@playwright/test';
import { ACCOUNTS, requireApi, signIn } from './helpers';

test.describe('field module', () => {
  test.beforeEach(async ({ page }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.supervisor);
  });

  test('supervisor sees only their own sites', async ({ page }) => {
    await page.goto('/admin');
    await expect(page.getByRole('heading', { name: 'سيتي سنتر الشارقة' })).toBeVisible();
    // The mosques belong to the other supervisor (1004).
    await expect(page.getByRole('heading', { name: 'جامع الشارقة' })).toHaveCount(0);
  });

  test('dashboard cards open the attendance list already filtered', async ({ page }) => {
    await page.goto('/admin');
    await page.getByRole('link', { name: /المتأخرون/ }).click();
    await expect(page).toHaveURL(/view=late/);
    await expect(page.getByRole('button', { name: 'المتأخرون فقط' })).toHaveClass(/on/);
  });

  test('a schedule cell shows its details and the actions for it', async ({ page }) => {
    await page.goto('/admin/schedule');
    await page.locator('.cell').first().click();
    await expect(page.getByRole('heading', { name: /معلومات اليوم|Day details/ })).toBeVisible();
    await expect(page.getByText('اختر الإجراء')).toBeVisible();
  });

  test('staffing a site from its page rejects a clash with another site', async ({ page }) => {
    await page.goto('/admin/locations');
    await page.getByRole('row', { name: /سيتي سنتر/ }).getByRole('button', { name: 'تعيين موظفين على الموقع' }).click();
    await expect(page.getByText('يخص هذا الموقع فقط')).toBeVisible();

    // 2004 already works the same morning hours at Mega Mall.
    await page.locator('.staff-row select').first().selectOption({ label: 'محمد عبدالله البلوشي' });
    await page.getByRole('button', { name: 'حفظ' }).click();
    await expect(page.getByText(/تداخل|سعة/)).toBeVisible();
  });

  test('approving a permission removes it from the pending badge', async ({ page }) => {
    await page.goto('/admin/requests');
    const badge = page.locator('nav .count').first();
    const before = Number((await badge.textContent()) ?? '0');

    await page.getByRole('row', { name: /إذن تأخير/ }).first().getByRole('button', { name: 'اعتماد' }).click();
    await expect(page.getByText('تم الحفظ')).toBeVisible();
    await expect.poll(async () => Number((await badge.textContent()) ?? '0')).toBeLessThan(before);
  });

  test('a dialog survives a drag that ends outside it', async ({ page }) => {
    await page.goto('/admin/locations');
    await page.getByRole('button', { name: 'موقع جديد' }).click();
    const name = page.getByLabel('الاسم بالعربية');
    await name.fill('اختبار السحب');

    // Press inside the field, release on the backdrop: the dialog must stay open.
    const box = (await name.boundingBox())!;
    await page.mouse.move(box.x + 10, box.y + box.height / 2);
    await page.mouse.down();
    await page.mouse.move(box.x - 400, box.y - 200, { steps: 10 });
    await page.mouse.up();

    await expect(name).toHaveValue('اختبار السحب');
    await page.keyboard.press('Escape');
    await expect(name).toHaveCount(0);
  });
});
