import { expect, test } from '@playwright/test';
import { uniqueEmail } from '../e2e/helpers/api';
import { acceptRequiredConsents, confirmRecoveryCodes, signUp } from '../e2e/helpers/flows';
import { msUntilNextStep, totp } from '../e2e/helpers/totp';

const STAFF_URL = process.env.COMPOSE_STAFF_URL ?? 'http://localhost:4200';

test('a patient signs up through the patient container', async ({ page }) => {
  await signUp(page, 'ar', uniqueEmail('compose'));
  await confirmRecoveryCodes(page, 'ar');
  await acceptRequiredConsents(page, 'ar');
});

test('the test inbox lists the codes that would have been sent', async ({ request }) => {
  const inbox = await request.get(`${process.env.E2E_API_URL ?? 'http://localhost:5080'}/dev/inbox`);
  expect(inbox.ok()).toBeTruthy();
  expect(await inbox.text()).toContain('Test inbox');
});

test('a supervisor signs in to the staff container', async ({ page }) => {
  await page.goto(`${STAFF_URL}/login`);
  await page.getByTestId('contact').fill('fahad@staff.tamaneen.dev');
  await page.getByTestId('password').fill('Tamaneen-Dev-2026!');
  await page.getByTestId('continue').click();
  await expect(page.getByTestId('code')).toBeVisible();
  // A fresh step, so a code used by another run is not replayed.
  await page.waitForTimeout(msUntilNextStep() + 500);
  await page.getByTestId('code').fill(totp());
  await page.getByTestId('verify').click();
  await expect(page.getByTestId('shell')).toBeVisible({ timeout: 20_000 });
});
