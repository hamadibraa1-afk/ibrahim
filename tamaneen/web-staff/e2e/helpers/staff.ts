import { type Page, expect } from '@playwright/test';
import { freshTotp } from './totp';

export const STAFF_PASSWORD = 'Tamaneen-Dev-2026!';
export const staffEmail = (name: string) => `${name}@staff.tamaneen.dev`;

/**
 * Signs in through the portal: password, then the authenticator code. A step this account already used
 * (e.g. by an earlier run) is refused, so on an error it waits for the next step and tries once more.
 */
export async function signIn(page: Page, name: string): Promise<void> {
  if (!page.url().includes('/login')) {
    await page.goto('/login');
  }
  await page.getByTestId('contact').fill(staffEmail(name));
  await page.getByTestId('password').fill(STAFF_PASSWORD);
  await page.getByTestId('continue').click();
  const code = page.getByTestId('code');
  await expect(code).toBeVisible();
  for (let attempt = 0; attempt < 2; attempt++) {
    await code.fill(await freshTotp(name));
    await page.getByTestId('verify').click();
    const outcome = await Promise.race([
      page.getByTestId('shell').waitFor({ timeout: 15_000 }).then(() => 'in' as const),
      page.getByTestId('login-error').waitFor({ timeout: 15_000 }).then(() => 'error' as const),
    ]);
    if (outcome === 'in') {
      return;
    }
  }
  await expect(page.getByTestId('shell'), 'signed in after a fresh authenticator code').toBeVisible();
}
