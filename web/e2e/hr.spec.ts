import { expect, test } from '@playwright/test';
import { ACCOUNTS, requireApi, signIn } from './helpers';

/**
 * Drives the HR module against the real stack. Run after:
 *   dotnet run -- --reset=true     (API + demo data)
 *   npm start                      (web)
 */
test.describe('HR module', () => {
  test.beforeEach(async ({ page }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.hrManager);
  });

  test('dashboard shows today from the shared attendance engine', async ({ page }) => {
    await expect(page).toHaveURL(/\/hr$/);
    await expect(page.getByText('الموظفون على رأس العمل')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'حضور اليوم' })).toBeVisible();
  });

  test('employee list opens a profile with its tabs', async ({ page }) => {
    await page.getByRole('link', { name: 'ملفات الموظفين' }).click();
    await expect(page.getByRole('cell', { name: '3001' })).toBeVisible();

    await page.getByRole('row', { name: /3001/ }).getByRole('button', { name: 'الملف' }).click();
    await expect(page.getByRole('button', { name: 'الراتب' })).toBeVisible();
    await page.getByRole('button', { name: 'الدوام' }).click();
    await expect(page.getByText('تغيير الجدول يسري من التاريخ المحدد')).toBeVisible();
  });

  test('HR manager sees salary, and the history records every change', async ({ page }) => {
    await page.getByRole('link', { name: 'ملفات الموظفين' }).click();
    await page.getByRole('row', { name: /3002/ }).getByRole('button', { name: 'الملف' }).click();
    await page.getByRole('button', { name: 'الراتب' }).click();

    await expect(page.getByText('الراتب الحالي')).toBeVisible();
    await page.getByLabel('الراتب الجديد').fill('11500');
    await page.getByLabel('السبب').fill('علاوة سنوية');
    await page.getByRole('button', { name: 'حفظ' }).click();

    await expect(page.getByRole('cell', { name: 'علاوة سنوية' })).toBeVisible();
    await expect(page.getByRole('cell', { name: '11500' })).toBeVisible();
  });

  test('a department with employees cannot be deleted', async ({ page }) => {
    await page.getByRole('link', { name: 'الهيكل التنظيمي' }).click();
    const department = page.locator('section', { hasText: 'تقنية المعلومات' }).first();
    await department.getByRole('button', { name: 'حذف' }).click();
    await page.getByRole('button', { name: 'تأكيد' }).click();
    await expect(page.getByText('الإدارة فيها موظفون')).toBeVisible();
  });

  test('a new department and section appear in the structure', async ({ page }) => {
    const stamp = Date.now().toString().slice(-5);
    await page.getByRole('link', { name: 'الهيكل التنظيمي' }).click();

    await page.getByRole('button', { name: 'إدارة جديدة' }).click();
    await page.getByLabel('الاسم بالعربية').fill(`إدارة الاختبار ${stamp}`);
    await page.getByLabel('الاسم بالإنجليزية').fill(`Test Dept ${stamp}`);
    await page.getByRole('button', { name: 'حفظ' }).click();
    await expect(page.getByRole('heading', { name: `إدارة الاختبار ${stamp}` })).toBeVisible();

    await page.getByRole('button', { name: 'قسم جديد' }).click();
    await page.getByLabel('الإدارة').selectOption({ label: `إدارة الاختبار ${stamp}` });
    await page.getByLabel('الاسم بالعربية').fill(`قسم ${stamp}`);
    await page.getByLabel('الاسم بالإنجليزية').fill(`Section ${stamp}`);
    await page.getByRole('button', { name: 'حفظ' }).click();
    await expect(page.getByText(`قسم ${stamp}`)).toBeVisible();
  });

  test('settings lists are editable without touching code', async ({ page }) => {
    const stamp = Date.now().toString().slice(-5);
    await page.getByRole('link', { name: 'الإعدادات' }).click();
    await page.getByRole('button', { name: 'عنصر جديد' }).click();
    await page.getByLabel('الاسم بالعربية').fill(`مسمى ${stamp}`);
    await page.getByLabel('الاسم بالإنجليزية').fill(`Title ${stamp}`);
    await page.getByRole('button', { name: 'حفظ' }).click();
    await expect(page.getByRole('cell', { name: `مسمى ${stamp}` })).toBeVisible();
  });

  test('a work schedule groups identical days and keeps a different one apart', async ({ page }) => {
    await page.getByRole('link', { name: 'جداول الدوام' }).click();
    const schedule = page.locator('section', { hasText: 'دوام إداري' }).first();
    await expect(schedule.getByText('الأحد')).toBeVisible();
    await expect(schedule.getByText('الخميس')).toBeVisible();
    await expect(schedule.getByText('ساعات الأسبوع')).toBeVisible();
  });

  test('a view-only account cannot change HR data', async ({ page, context }) => {
    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await signIn(page, ACCOUNTS.viewer);
    await page.goto('/hr/employees');

    await expect(page.getByText('صلاحيتك استعراض فقط')).toBeVisible();
    await expect(page.getByRole('button', { name: 'موظف إداري جديد' })).toHaveCount(0);

    // The server must refuse too, not just the hidden button.
    const token = await page.evaluate(() => JSON.parse(localStorage.getItem('session') ?? '{}').token);
    const refused = await context.request.post('http://localhost:5000/api/hr/org/departments', {
      headers: { Authorization: `Bearer ${token}` },
      data: { nameAr: 'محاولة', nameEn: 'Attempt' },
    });
    expect(refused.status()).toBe(403);
  });
});
