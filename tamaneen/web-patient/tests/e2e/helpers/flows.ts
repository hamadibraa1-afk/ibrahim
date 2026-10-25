import { readFileSync } from 'node:fs';
import path from 'node:path';
import { expect, type Page } from '@playwright/test';
import { STRONG_PASSWORD, codeFromOutbox } from './api';

export type Locale = 'ar' | 'en';

const MESSAGES_DIR = path.resolve(__dirname, '../../../messages');
const cache = new Map<string, Record<string, unknown>>();

/** The app's own copy, so selectors follow the current language: t('ar', 'signup.submit'). */
export function t(locale: Locale, key: string, values: Record<string, string | number> = {}): string {
  const [namespace, ...rest] = key.split('.');
  const file = `${locale}/${namespace}`;
  if (!cache.has(file)) cache.set(file, JSON.parse(readFileSync(path.join(MESSAGES_DIR, `${file}.json`), 'utf8')) as Record<string, unknown>);
  let node: unknown = cache.get(file)![namespace!];
  for (const part of rest) node = (node as Record<string, unknown>)?.[part];
  if (typeof node !== 'string') throw new Error(`No message ${locale}:${key}`);
  return node.replace(/\{(\w+)\}/g, (_, name: string) => String(values[name] ?? `{${name}}`));
}

/** <html lang dir> for the language. */
export async function expectDirection(page: Page, locale: Locale): Promise<void> {
  await expect(page.locator('html')).toHaveAttribute('lang', locale);
  await expect(page.locator('html')).toHaveAttribute('dir', locale === 'ar' ? 'rtl' : 'ltr');
}

/** P01 → P15 (adult, Riyadh) → P02 (e-mail + strong password) → P16 (code from the outbox). Ends on P17. */
export async function signUp(page: Page, locale: Locale, email: string, password: string = STRONG_PASSWORD): Promise<void> {
  await page.goto(`/${locale}`);
  await expectDirection(page, locale);
  await page.getByTestId('cta-start').click();

  // P15 eligibility
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/eligibility$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(t(locale, 'eligibility.title'));
  await expectDirection(page, locale);
  await page.getByRole('radio', { name: t(locale, 'ui.yes'), exact: true }).click();
  await page.getByLabel(t(locale, 'eligibility.cityLabel')).selectOption('RUH');
  await page.getByRole('button', { name: t(locale, 'ui.continue') }).click();

  // P02 sign-up
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/signup$`));
  await expectDirection(page, locale);
  await expect(page.getByLabel(t(locale, 'signup.nickname'))).not.toHaveValue('');
  await page.getByRole('radio', { name: t(locale, 'signup.email') }).click();
  await page.getByRole('textbox', { name: t(locale, 'signup.email') }).fill(email);
  await page.getByLabel(t(locale, 'signup.password'), { exact: true }).fill(password);
  await expect(page.getByTestId('password-strength')).toHaveAttribute('data-strength', '4');
  await page.getByRole('button', { name: t(locale, 'signup.submit') }).click();

  // P16 verify
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/verify$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(t(locale, 'verify.title'));
  await expectDirection(page, locale);
  const code = await codeFromOutbox(email.toLowerCase());
  await page.getByLabel(t(locale, 'verify.codeLabel')).fill(code);
  await page.getByRole('button', { name: t(locale, 'verify.submit') }).click();

  // P17 recovery codes
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/recovery-codes$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(t(locale, 'recoveryCodes.title'));
  await expectDirection(page, locale);
}

/** P17 → P03: download the codes, confirm they are saved, continue (PB-09 needs both). */
export async function confirmRecoveryCodes(page: Page, locale: Locale): Promise<void> {
  await expect(page.getByTestId('recovery-codes').getByRole('listitem')).toHaveCount(10);
  const next = page.getByRole('button', { name: t(locale, 'ui.continue') });
  await expect(next).toBeDisabled();
  await page.getByLabel(t(locale, 'recoveryCodes.savedCheck')).check();
  await expect(next).toBeDisabled();
  const download = page.waitForEvent('download');
  await page.getByRole('button', { name: t(locale, 'recoveryCodes.download') }).click();
  await download;
  await next.click();
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/consents$`));
}

/** P03: accept the required consents (platform, AI, sharing with the therapist) and continue. */
export async function acceptRequiredConsents(page: Page, locale: Locale): Promise<void> {
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(t(locale, 'consents.title'));
  await expectDirection(page, locale);
  const submit = page.getByRole('button', { name: t(locale, 'consents.submit') });
  await expect(submit).toBeDisabled();
  for (const key of ['platform', 'ai', 'share_with_therapist']) {
    const row = page.getByTestId(`consent-${key}`);
    // PB-15: a required consent can be ticked only after its full text was opened.
    await expect(row.getByRole('checkbox')).toBeDisabled();
    await row.getByRole('button', { name: t(locale, 'consents.readFull') }).click();
    await row.getByRole('checkbox').check();
  }
  await expect(page.getByTestId('consent-research').getByRole('checkbox')).not.toBeChecked();
  await submit.click();
  await expect(page).toHaveURL(new RegExp(`/${locale}/start/safety-info$`));
}

/** P08: pick a day (from `minDayIndex` on) and a random time, check the hold countdown, confirm. Ends on P26. */
export async function bookASlot(page: Page, locale: Locale, minDayIndex: number): Promise<void> {
  await expect(page).toHaveURL(new RegExp(`/${locale}/therapists/[^/]+/book`));
  const days = page.getByRole('list', { name: t(locale, 'booking.daysLabel') }).getByRole('button');
  await expect(days.first()).toBeVisible();
  const dayCount = await days.count();
  expect(dayCount, 'days with free times').toBeGreaterThan(minDayIndex);
  const dayIndex = minDayIndex + Math.floor(Math.random() * (dayCount - minDayIndex));
  await days.nth(dayIndex).click();
  await expect(days.nth(dayIndex)).toHaveAttribute('aria-pressed', 'true');

  const times = page.getByRole('list', { name: t(locale, 'booking.timesLabel') }).getByRole('button');
  const timeCount = await times.count();
  expect(timeCount).toBeGreaterThan(0);
  const confirm = page.getByRole('button', { name: t(locale, 'booking.confirm') });
  await expect(confirm).toBeDisabled();
  await times.nth(Math.floor(Math.random() * timeCount)).click();

  const countdown = page.getByTestId('hold-countdown');
  await expect(countdown).toBeVisible();
  await expect(countdown).toHaveText(/^\d{1,2}:\d{2}$/);
  const first = await countdown.textContent();
  await expect(countdown).not.toHaveText(first!, { timeout: 5_000 });
  await expect(confirm).toBeEnabled();
  await confirm.click();

  await expect(page).toHaveURL(new RegExp(`/${locale}/appointments/[^/]+/confirmed$`));
  await expect(page.getByRole('heading', { level: 1 })).toHaveText(t(locale, 'bookConfirmed.title'));
}
