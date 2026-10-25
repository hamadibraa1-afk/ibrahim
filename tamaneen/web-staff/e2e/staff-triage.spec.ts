import { expect, test } from '@playwright/test';
import { completeIntakeByForm, signUpPatient, triageState } from './helpers/patient-api';
import { signIn } from './helpers/staff';

/**
 * S03 end to end: a patient on the intake path submits the form intake; Fahad (supervisor) finds the case in
 * the triage queue, opens it (which claims it), sees the patient's summary and the suggested therapists, and
 * approves; the patient then sees that matches are ready (P36 → P07).
 */
test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

test('a supervisor reviews and approves an intake case; the patient gets matches', async ({ page, request }) => {
  const patient = await signUpPatient(request);
  await completeIntakeByForm(request, patient);
  const pseudonymId = (JSON.parse(Buffer.from(patient.accessToken.split('.')[1], 'base64url').toString()) as { pid: string }).pid;

  await signIn(page, 'fahad');
  await page.goto('/triage');
  const item = page.getByTestId('queue-item').filter({ hasText: pseudonymId });
  await expect(item).toBeVisible();
  await item.click();

  await expect(page.getByTestId('case-pseudonym')).toHaveText(pseudonymId);
  await expect(page.getByTestId('claim-bar')).toBeVisible();
  await expect(page.getByText('أشعر بقلق وضغط في العمل منذ شهرين')).toBeVisible();
  await expect(page.getByTestId('suggested').first()).toBeVisible();

  await page.getByTestId('approve').click();
  await expect(page.getByText('اعتُمدت الحالة، ويرى المريض الاقتراحات الآن.')).toBeVisible();
  await expect(page.getByTestId('queue-item').filter({ hasText: pseudonymId })).toHaveCount(0);

  await expect.poll(async () => (await triageState(request, patient)).suggestionsReady).toBe(true);
});
