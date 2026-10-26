import { type Page, expect, test } from '@playwright/test';
import { type TestPatient, onboardAndBookWithSara, signUpPatient } from './helpers/patient-api';
import { signIn } from './helpers/staff';

/**
 * Phase 5 care record: Sara opens her new patient's overview (S14), moves through the tabs to the clinical file
 * (S02), assigns homework from the library (S16) and writes the safety plan with the patient (S35).
 */
test.describe.configure({ mode: 'serial' });
test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

let page: Page;
let patient: TestPatient;
let pseudonymId: string;

test.beforeAll(async ({ browser }) => {
  page = await browser.newPage();
});
test.afterAll(async () => {
  await page.close();
});

test('a patient books with Sara and Sara opens the overview', async ({ request }) => {
  patient = await signUpPatient(request);
  await onboardAndBookWithSara(request, patient);
  pseudonymId = (JSON.parse(Buffer.from(patient.accessToken.split('.')[1], 'base64url').toString()) as { pid: string }).pid;
  await signIn(page, 'sara');
  await page.goto(`/patients/${pseudonymId}`);
  await expect(page.getByTestId('overview-pseudonym')).toHaveText(pseudonymId);
  await expect(page.getByTestId('no-mood')).toHaveText('لا قياسات بعد.');
  await expect(page.getByTestId('no-plan')).toHaveText('لا خطة بعد.');
  await page.getByTestId('tab-file').click();
  await expect(page).toHaveURL(new RegExp(`/patients/${pseudonymId}/file$`));
  await expect(page.getByTestId('file-pseudonym')).toHaveText(pseudonymId);
});

test('Sara assigns the breathing exercise from the library', async () => {
  await page.getByTestId('tab-homework').click();
  await expect(page.getByTestId('no-homework')).toBeVisible();
  await page.getByTestId('template-breathing').click();
  await expect(page.getByTestId('hw-title')).toHaveValue('تمرين التنفس قبل النوم');
  await page.getByTestId('hw-preview-button').click();
  await expect(page.getByTestId('hw-preview')).toContainText('تنفس من أنفك');
  await page.getByTestId('hw-save').click();
  await expect(page.getByTestId('homework-row')).toContainText('تمرين التنفس قبل النوم');
});

test('Sara writes the safety plan, which the patient then reads with version 1', async ({ request }) => {
  await page.getByTestId('tab-safety-plan').click();
  await expect(page.getByTestId('safety-new')).toBeVisible();
  await page.getByTestId('section-warningSigns').fill('أسهر كثيرًا وأنعزل في غرفتي.');
  await page.getByTestId('safety-save').click();
  await expect(page.getByTestId('safety-version')).toContainText('الإصدار 1');
  const plan = await request.get('http://localhost:5080/v1/safety-plan', { headers: { Authorization: `Bearer ${patient.accessToken}` } });
  expect(plan.headers()['etag']).toBe('"1"');
  expect(((await plan.json()) as { sections: { warningSigns: string } }).sections.warningSigns).toBe('أسهر كثيرًا وأنعزل في غرفتي.');
});
