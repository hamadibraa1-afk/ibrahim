import { expect, test } from '@playwright/test';
import { closeCrisisEvent, openCrisisEvent, pseudonymOf, reviewTriage, uniqueEmail } from './helpers/api';
import { acceptRequiredConsents, bookASlot, confirmRecoveryCodes, expectDirection, signUp } from './helpers/flows';

test.describe.configure({ mode: 'serial' });
test.use({ locale: 'ar-SA', timezoneId: 'Asia/Riyadh' });

test.describe('Arabic patient journey', () => {
  test.skip(({ isMobile }) => !isMobile, 'The full journey runs on the mobile project only.');
  test.skip(!process.env.E2E_API_URL, 'Set E2E_API_URL to a running Tamaneen.Api.');

  test('from the welcome page to a booked session, a language switch and a crisis check-in', async ({ page }) => {
    test.setTimeout(240_000);
    const ar = () => expectDirection(page, 'ar');
    const email = uniqueEmail('ar');

    // P01 → P15 → P02 → P16 → P17
    await signUp(page, 'ar', email);
    await confirmRecoveryCodes(page, 'ar');

    // P03 consents
    await acceptRequiredConsents(page, 'ar');

    // P21 safety info
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('كيف نصل إليك عند الحاجة؟');
    await ar();
    await page.getByLabel('رقم جوالك للاتصال').fill('0501234567');
    await page.getByLabel('اسم شخص تثق به').fill('أختي');
    await page.getByLabel('رقم جواله').fill('0507654321');
    await page.getByRole('button', { name: 'حفظ والمتابعة' }).click();

    // P22 choose the intake chat
    await expect(page).toHaveURL(/\/ar\/start\/path$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('كيف تحب أن تبدأ؟');
    await ar();
    await page.getByTestId('path-intake').click();

    // P04 intake chat
    await expect(page).toHaveURL(/\/ar\/intake$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('رفيق · مساعد التعارف');
    await ar();
    const composer = page.getByRole('textbox', { name: 'رسالتك' });
    const send = page.getByRole('button', { name: 'إرسال', exact: true });
    const finish = page.getByTestId('finish');
    const log = page.getByTestId('intake-log');
    await expect(finish).toHaveAttribute('aria-disabled', 'true');

    for (const text of ['أشعر بقلق شديد من العمل منذ شهرين', 'ولا أنام جيدًا، أستيقظ كثيرًا في الليل']) {
      await composer.fill(text);
      await send.click();
      await expect(log.getByText(text)).toBeVisible();
      await expect(page.getByTestId('typing')).toHaveCount(0);
      await expect(composer).toBeEnabled();
    }

    const card = page.getByTestId('question-card');
    for (const [instrument, total] of [['PHQ-9', 9], ['GAD-7', 7]] as const) {
      for (let item = 1; item <= total; item++) {
        await expect(card).toContainText(`${instrument} · السؤال ${item} من ${total}`);
        // Item 9 of the PHQ-9 asks about self-harm: "never" keeps this journey out of the crisis path.
        const answer = instrument === 'PHQ-9' && item === 9 ? 'أبدًا' : 'عدة أيام';
        await card.getByRole('radio', { name: answer }).click();
      }
    }
    await expect(card).toHaveCount(0);

    const last = 'هذا كل ما أردت قوله الآن';
    await composer.fill(last);
    await send.click();
    await expect(log.getByText(last)).toBeVisible();
    await expect(page.getByTestId('typing')).toHaveCount(0);
    await expect(finish).toHaveAttribute('aria-disabled', 'false');
    await finish.click();

    // P05 summary: edit one section, then submit
    await expect(page).toHaveURL(/\/ar\/intake\/summary$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('هل فهمناك بشكل صحيح؟');
    await ar();
    const concern = page.getByTestId('section-concern');
    if ((await page.getByTestId('edit-concern').count()) === 0) {
      await concern.getByRole('button', { name: /^(تعديل|اكتب): ما الذي يضايقك$/ }).click();
    }
    await page.getByTestId('edit-concern').fill('قلق مستمر بسبب العمل، وقلة نوم منذ شهرين.');
    await concern.getByRole('button', { name: 'حفظ', exact: true }).click();
    await expect(concern).toContainText('قلق مستمر بسبب العمل، وقلة نوم منذ شهرين.');
    await expect(concern).toContainText('صحّحته أنت');
    await page.getByRole('button', { name: 'إرسال إلى فريق الرعاية' }).click();
    await page.getByRole('dialog').getByRole('button', { name: 'نعم، أرسل' }).click();

    // P36 waiting for review
    await expect(page).toHaveURL(/\/ar\/waiting$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('شكرًا لثقتك');
    await ar();

    // The supervisor reviews the file (staff UI is out of scope here).
    const pseudonymId = await pseudonymOf(page);
    await reviewTriage(pseudonymId);

    // P07 matches: three therapists, with reasons
    await page.goto('/ar/matches');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('اخترنا لك ثلاثة أخصائيين');
    await ar();
    const suggestions = page.getByRole('list', { name: 'الأخصائيون المقترحون' }).locator(':scope > li');
    await expect(suggestions).toHaveCount(3);
    await expect(suggestions.first()).toContainText(/لماذا اقترحناه(ا)?:/);
    await expect(suggestions.first()).toContainText('خبرة في');
    await expect(suggestions.nth(1)).toContainText(/لماذا اقترحناه(ا)?:/);
    const name = (await suggestions.first().getByRole('heading', { level: 2 }).textContent())!.trim();

    // P25 profile
    await suggestions.first().getByRole('link', { name: 'عرض الملف والمواعيد' }).click();
    await expect(page).toHaveURL(/\/ar\/therapists\/[^/]+$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
    await ar();
    await page.getByRole('link', { name: 'اختيار موعد' }).click();

    // P08 booking → P26 confirmed
    await ar();
    await bookASlot(page, 'ar', 1);
    await ar();
    await expect(page.getByText(`مع ${name}`)).toBeVisible();

    // P27 appointments
    await page.goto('/ar/appointments');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('مواعيدي');
    await ar();
    const upcoming = page.getByRole('region', { name: 'القادمة' });
    await expect(upcoming.getByRole('link')).toHaveCount(1);
    await expect(upcoming.getByRole('link')).toContainText(name);
    await expect(upcoming.getByRole('link')).toContainText('مؤكد');

    // P09 home shows the next session
    await page.goto('/ar/home?from=e2e');
    await ar();
    const next = page.getByRole('region', { name: `جلستك مع ${name}` });
    await expect(next).toBeVisible();
    await expect(next.getByRole('link', { name: `تفاصيل جلستك مع ${name}` })).toBeVisible();

    // T-501: persist the wish to continue through the real BFF/API and a reload.
    await page.getByTestId('continue-care-submit').click();
    await expect(page.getByTestId('continue-care').getByRole('status')).toBeVisible();
    await page.reload();
    await expect(page.getByTestId('continue-care').getByRole('status')).toBeVisible();
    await expect(page.getByTestId('continue-care-submit')).toHaveCount(0);

    // Language switch on P09: same page and query, English and left-to-right
    await page.getByTestId('language-toggle').click();
    await expect(page).toHaveURL(/\/en\/home\?from=e2e$/);
    await expectDirection(page, 'en');
    await expect(page.getByTestId('continue-care').getByRole('status')).toContainText('Your request to continue care is recorded.');
    await expect(page.getByRole('region', { name: /^Your session with / })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Browse specialists' })).toHaveCount(0);
    await expect(page.getByTestId('help-now')).toHaveText('Help now');
    await page.getByTestId('language-toggle').click();
    await expect(page).toHaveURL(/\/ar\/home\?from=e2e$/);
    await ar();
    await expect(page.getByTestId('help-now')).toHaveText('مساعدة فورية');

    // P30 check-in with a note that the crisis gate must catch → P06
    await page.goto('/ar/check-in');
    await ar();
    await page.getByLabel('ملاحظة').fill('ما عاد أتحمل، أبي أموت الليلة');
    await page.getByRole('button', { name: 'حفظ', exact: true }).click();
    await expect(page).toHaveURL(/\/ar\/crisis$/, { timeout: 15_000 });
    await ar();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('أنت لست وحدك');
    const emergency = page.locator('a[href="tel:911"]');
    await expect(emergency.first()).toBeVisible();
    await expect(emergency.first()).toContainText('911');

    // While the event is open the patient is locked out of the chat (GET /v1/me → chatLocked: true).
    await expect.poll(async () => (await page.evaluate(async () => (await fetch('/api/v1/me')).json())).chatLocked).toBe(true);

    // The supervisor closes the event; home works again.
    const event = await openCrisisEvent(pseudonymId);
    await closeCrisisEvent(event.eventId, 'E2E: patient reached and safe.');
    await page.reload();
    await expect.poll(async () => (await page.evaluate(async () => (await fetch('/api/v1/me')).json())).chatLocked).toBe(false);
    await page.goto('/ar/home');
    await expect(page).toHaveURL(/\/ar\/home$/);
    await ar();
    await expect(page.getByRole('region', { name: `جلستك مع ${name}` })).toBeVisible();
  });
});
