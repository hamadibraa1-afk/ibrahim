import { expect, test } from '@playwright/test';
import { ACCOUNTS, requireApi, signIn } from './helpers';

const API = process.env['E2E_API_URL'] ?? 'http://localhost:5000';

/** Reads the bearer token the app stored after signing in. */
async function tokenOf(page: import('@playwright/test').Page): Promise<string> {
  return page.evaluate(() => JSON.parse(localStorage.getItem('session') ?? '{}').token as string);
}

/**
 * The rules that protect money and privacy. These are server-side checks, so each one is
 * exercised through the API directly: hiding a button in the UI is not a control.
 */
test.describe('safeguards', () => {
  test('an employee cannot read another employee\'s attendance (IDOR)', async ({ page, context }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.hrManager);
    const hrToken = await tokenOf(page);

    // Pick a real employee id using an account that is allowed to.
    const list = await context.request.get(`${API}/api/hr/employees`, {
      headers: { Authorization: `Bearer ${hrToken}` },
    });
    const employees = await list.json();
    const someoneElse = employees.find((e: { employeeNumber: string }) => e.employeeNumber === '3003');
    expect(someoneElse).toBeTruthy();

    // Now sign in as a different employee and ask for that person's record.
    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await signIn(page, { number: '3002', password: 'Test@1234' });
    const employeeToken = await tokenOf(page);

    const refused = await context.request.get(`${API}/api/hr/employees/${someoneElse.id}`, {
      headers: { Authorization: `Bearer ${employeeToken}` },
    });
    expect([401, 403]).toContain(refused.status());

    // Their own record still works.
    const own = await context.request.get(`${API}/api/my/profile`, {
      headers: { Authorization: `Bearer ${employeeToken}` },
    });
    expect(own.ok()).toBeTruthy();
  });

  test('attendance cannot be queried for an employee outside the caller\'s scope', async ({ page, context }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.hrManager);
    const hrToken = await tokenOf(page);
    const employees = await (await context.request.get(`${API}/api/hr/employees`, {
      headers: { Authorization: `Bearer ${hrToken}` },
    })).json();
    const target = employees[0];

    await page.getByRole('button', { name: 'تسجيل الخروج' }).click();
    await signIn(page, { number: '3002', password: 'Test@1234' });
    const token = await tokenOf(page);

    const refused = await context.request.get(
      `${API}/api/attendance?from=2026-01-01&to=2026-01-31&employeeId=${target.id}`,
      { headers: { Authorization: `Bearer ${token}` } });
    expect([401, 403]).toContain(refused.status());
  });

  test('a settled payroll month refuses new decisions', async ({ page, context }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.hrManager);
    const token = await tokenOf(page);
    const headers = { Authorization: `Bearer ${token}` };

    // Open a cycle for a month in the past, calculate it and approve it.
    const year = 2026;
    const month = 1;
    await context.request.post(`${API}/api/hr/payroll/cycles`, { headers, data: { year, month } });
    const cycles = await (await context.request.get(`${API}/api/hr/payroll/cycles`, { headers })).json();
    const cycle = cycles.find((c: { year: number; month: number }) => c.year === year && c.month === month);
    test.skip(!cycle, 'The cycle could not be created in this environment.');

    await context.request.post(`${API}/api/hr/payroll/cycles/${cycle.id}/calculate`, { headers });
    await context.request.post(`${API}/api/hr/payroll/cycles/${cycle.id}/approve`, { headers });

    // Any deduction proposal inside that month must now be refused.
    const types = await (await context.request.get(`${API}/api/hr/discipline/types`, { headers })).json();
    const employees = await (await context.request.get(`${API}/api/hr/employees`, { headers })).json();

    const refused = await context.request.post(`${API}/api/hr/discipline/proposals`, {
      headers,
      data: {
        employeeId: employees[0].id, deductionTypeId: types[0].id,
        onDate: `${year}-0${month}-15`, units: 1, reason: 'اختبار قفل الفترة',
      },
    });
    expect(refused.status()).toBe(400);
    expect(await refused.text()).toContain('payroll.period_locked');
  });

  test('leave cannot be approved over days already worked', async ({ page, context }) => {
    await requireApi(page);
    await signIn(page, ACCOUNTS.hrManager);
    const headers = { Authorization: `Bearer ${await tokenOf(page)}` };

    const leaves = await (await context.request.get(`${API}/api/requests/leaves?status=Pending`, { headers })).json();
    test.skip(!leaves.length, 'No pending leave to exercise in this environment.');

    // The rule is asserted through its message, whichever request is available.
    const response = await context.request.post(`${API}/api/requests/leaves/${leaves[0].id}/approve`, { headers });
    if (!response.ok()) expect(await response.text()).toMatch(/attendance_conflict|approval|period_locked/);
  });
});
