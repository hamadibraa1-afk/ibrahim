import { expect, test } from '@playwright/test';
import { uniqueEmail } from './helpers/api';
import { acceptRequiredConsents, bookASlot, confirmRecoveryCodes, expectDirection, signUp, t } from './helpers/flows';

test.use({ locale: 'en-US', timezoneId: 'Asia/Riyadh' });

test.describe('English patient', () => {
  test.skip(!process.env.E2E_API_URL, 'Set E2E_API_URL to a running Tamaneen.Api.');

  test('a weak password shows an English validation error on P02', async ({ page }) => {
    await page.goto('/en/start/eligibility');
    await expectDirection(page, 'en');
    await page.getByRole('radio', { name: 'Yes', exact: true }).click();
    await page.getByLabel(t('en', 'eligibility.cityLabel')).selectOption('RUH');
    await page.getByRole('button', { name: 'Continue' }).click();
    await expect(page).toHaveURL(/\/en\/start\/signup$/);

    await page.getByRole('radio', { name: 'Email' }).click();
    await page.getByRole('textbox', { name: 'Email' }).fill(uniqueEmail('weak'));
    const password = page.getByLabel('Password', { exact: true });
    await password.fill('short1');
    await expect(page.getByText('Too short: at least 12 characters, and not a leaked password')).toBeVisible();
    await page.getByRole('button', { name: 'Create account' }).click();
    await expect(page.getByRole('alert').filter({ hasText: 'Your password needs at least 12 characters.' })).toBeVisible();
    await expect(password).toHaveAttribute('aria-invalid', 'true');
    await expect(page).toHaveURL(/\/en\/start\/signup$/);
  });

  test('Help now reaches the emergency resources with 911', async ({ page }) => {
    await page.goto('/en');
    await page.getByTestId('help-now').click();
    await expect(page).toHaveURL(/\/en\/crisis-resources$/);
    await expectDirection(page, 'en');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('If you need help right now');
    await expect(page.locator('a[href="tel:911"]')).toContainText('911');
  });

  test('signs up, books directly after the safety check, and manages the appointment', async ({ page }) => {
    test.setTimeout(180_000);
    await signUp(page, 'en', uniqueEmail('en'));
    await confirmRecoveryCodes(page, 'en');
    await acceptRequiredConsents(page, 'en');

    // P21: skipped on this path
    await page.getByRole('button', { name: 'Skip for now' }).click();

    // P22 → book directly
    await expect(page).toHaveURL(/\/en\/start\/path$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('How would you like to start?');
    await page.getByTestId('path-direct').click();

    // P23 safety check: every answer "No"
    await expect(page).toHaveURL(/\/en\/start\/safety-check$/);
    await expectDirection(page, 'en');
    const count = page.getByTestId('question-count');
    await expect(count).toHaveText(/^1 of \d+$/);
    const total = Number((await count.textContent())!.split(' of ')[1]);
    for (let i = 1; i <= total; i++) {
      if (i < total) await expect(count).toHaveText(`${i} of ${total}`);
      await page.getByRole('radio', { name: 'No', exact: true }).click();
    }

    // P24 directory and filter chips
    await expect(page).toHaveURL(/\/en\/therapists$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('All therapists');
    const filters = page.getByRole('group', { name: 'Filter results' });
    const list = page.getByRole('list', { name: 'Therapists' });
    await expect(list.getByRole('link').first()).toBeVisible();
    const all = await list.getByRole('link').count();

    const anxiety = filters.getByRole('button', { name: 'Anxiety' });
    await anxiety.click();
    await expect(anxiety).toHaveAttribute('aria-pressed', 'true');
    await expect(page).toHaveURL(/specialty=anxiety/);
    await expect(page.getByRole('status').filter({ hasText: /therapist/i })).toBeVisible();
    await expect(list).not.toHaveAttribute('aria-busy', 'true');
    const female = filters.getByRole('button', { name: 'Female therapist' });
    await female.click();
    await expect(page).toHaveURL(/gender=female/);
    await expect(list).not.toHaveAttribute('aria-busy', 'true');
    expect(await list.getByRole('link').count()).toBeLessThanOrEqual(all);

    await filters.getByRole('button', { name: 'Clear search and filters' }).click();
    await expect(anxiety).toHaveAttribute('aria-pressed', 'false');
    await expect(page).toHaveURL(/\/en\/therapists$/);
    await expect(list.getByRole('link')).toHaveCount(all);

    // P25 profile
    const first = list.getByRole('link').first();
    await first.click();
    await expect(page).toHaveURL(/\/en\/therapists\/[^/]+$/);
    const name = (await page.getByRole('heading', { level: 1 }).textContent())!.trim();
    await expect(page.getByRole('region', { name: 'Session details' })).toBeVisible();
    await page.getByRole('link', { name: 'Choose a time' }).click();

    // P08 → P26 (two days ahead at least, so the appointment can still be changed)
    await bookASlot(page, 'en', 2);
    await expect(page.getByText(`With ${name}`, { exact: false })).toBeVisible();

    // P27 → P28 detail
    await page.goto('/en/appointments');
    await page.getByRole('region', { name: 'Upcoming' }).getByRole('link').first().click();
    await expect(page).toHaveURL(/\/en\/appointments\/[^/]+$/);
    await expectDirection(page, 'en');
    // P28: joining opens 10 minutes before; until then the button says when, and the waiting room can be prepared.
    await expect(page.getByRole('button', { name: /^Join the session · / })).toBeDisabled();

    const cancel = page.getByRole('button', { name: 'Cancel appointment' });
    await expect(cancel).toBeEnabled();
    await cancel.click();
    const dialog = page.getByRole('dialog', { name: 'Cancel this appointment?' });
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Keep it' })).toBeFocused();
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(cancel).toBeFocused();

    // Give the time back so repeated runs keep finding free slots.
    await cancel.click();
    // P28 asks why before cancelling.
    await dialog.getByText('It clashes with something else').click();
    await dialog.getByRole('button', { name: 'Yes, cancel it' }).click();
    await expect(page.getByText('Your appointment is cancelled.', { exact: false })).toBeVisible();
  });
});
