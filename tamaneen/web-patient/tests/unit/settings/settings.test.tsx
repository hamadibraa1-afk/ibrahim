import { act, fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import type { Me, NotificationPrefs, SafetyInfo, SecurityOverview } from '@/lib/api/types';
import { renderWithIntl } from '../intl';

const mocks = vi.hoisted(() => ({ api: vi.fn(), authApi: vi.fn(), replace: vi.fn(), refresh: vi.fn(), assign: vi.fn(), locationReplace: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api, authApi: mocks.authApi }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
  useRouter: () => ({ replace: mocks.replace, push: vi.fn(), refresh: mocks.refresh }),
  usePathname: () => '/settings',
}));

import { NotificationsForm } from '@/components/settings/NotificationsForm';
import { ProfileForm } from '@/components/settings/ProfileForm';
import { QuickExit } from '@/components/settings/QuickExit';
import { SafetyForm } from '@/components/settings/SafetyForm';
import { SecurityPanel } from '@/components/settings/SecurityPanel';
import { SettingsMenu } from '@/components/settings/SettingsMenu';

const realLocation = window.location;
beforeAll(() => {
  Object.defineProperty(window, 'location', {
    configurable: true,
    value: { ...realLocation, protocol: 'http:', assign: mocks.assign, replace: mocks.locationReplace },
  });
});
afterAll(() => {
  Object.defineProperty(window, 'location', { configurable: true, value: realLocation });
});
beforeEach(() => {
  for (const m of Object.values(mocks)) m.mockReset();
  mocks.authApi.mockResolvedValue(undefined);
  document.cookie = 'tamaneen_qe=; path=/; max-age=0';
});

const me: Me = {
  pseudonymId: 'P-1',
  nickname: 'نخلة هادئة',
  language: 'ar',
  timeZone: 'Asia/Riyadh',
  onboarding: { consentsDone: true, safetyInfoDone: true, path: 'direct', screeningDone: true, intakeStatus: 'none', triageStatus: 'none' },
  chatLocked: false,
};

describe('SettingsMenu', () => {
  it('links every section and marks the current one', () => {
    renderWithIntl(<SettingsMenu current="profile" />, 'ar');
    const nav = screen.getByRole('navigation', { name: 'أقسام الإعدادات' });
    expect(within(nav).getByRole('link', { name: 'الملف الشخصي' })).toHaveAttribute('aria-current', 'page');
    expect(within(nav).getByRole('link', { name: 'الأمان' })).toHaveAttribute('href', '/settings/security');
    expect(within(nav).getByRole('link', { name: 'الخصوصية وبياناتي' })).toHaveAttribute('href', '/privacy');
    expect(within(nav).getByRole('link', { name: 'الإشعارات' })).toHaveAttribute('href', '/settings/notifications');
    expect(within(nav).getByRole('link', { name: 'جهة الطوارئ' })).toHaveAttribute('href', '/settings/safety');
  });
});

describe('ProfileForm', () => {
  it('switches to English: stores the language, then reloads the page under the other locale', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<ProfileForm me={me} />, 'ar');
    expect(screen.getByRole('radio', { name: 'العربية' })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(screen.getByRole('radio', { name: 'English' }));
    expect(mocks.api).toHaveBeenCalledWith('me/language', { method: 'PUT', body: { language: 'en' } });
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/settings', { locale: 'en' }));
  });

  it('switches back to Arabic from English', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<ProfileForm me={{ ...me, language: 'en' }} />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: 'العربية' }));
    expect(mocks.api).toHaveBeenCalledWith('me/language', { method: 'PUT', body: { language: 'ar' } });
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/settings', { locale: 'ar' }));
  });

  it('does not reload when the language call fails', async () => {
    const { ApiError } = await import('@/lib/api/errors');
    mocks.api.mockRejectedValue(new ApiError(500, { title: 'تعذّر الحفظ', status: 500 }));
    renderWithIntl(<ProfileForm me={me} />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'English' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر الحفظ');
    expect(mocks.replace).not.toHaveBeenCalled();
  });

  it('saves the nickname and time zone', async () => {
    mocks.api.mockResolvedValue({ ...me, nickname: 'غيمة', timeZone: 'Asia/Dubai' });
    renderWithIntl(<ProfileForm me={me} />, 'ar');
    const nickname = screen.getByRole('textbox', { name: 'الاسم المستعار' });
    await userEvent.clear(nickname);
    await userEvent.type(nickname, 'غيمة');
    await userEvent.selectOptions(screen.getByRole('combobox', { name: 'المنطقة الزمنية' }), 'Asia/Dubai');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    expect(mocks.api).toHaveBeenCalledWith('me/profile', { method: 'PUT', body: { nickname: 'غيمة', timeZone: 'Asia/Dubai' } });
    expect(await screen.findByText('حفظنا ملفك الشخصي.')).toBeInTheDocument();
  });

  it('labels the time zones in English and requires a nickname', async () => {
    renderWithIntl(<ProfileForm me={me} />, 'en');
    expect(screen.getByRole('option', { name: 'Riyadh (pilot region time)' })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: 'Muscat' })).toBeInTheDocument();
    await userEvent.clear(screen.getByRole('textbox', { name: 'Nickname' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Enter a nickname.');
    expect(mocks.api).not.toHaveBeenCalled();
  });

  it('signs out through the BFF and returns to the welcome page', async () => {
    renderWithIntl(<ProfileForm me={me} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الخروج' }));
    expect(mocks.authApi).toHaveBeenCalledWith('logout');
    await waitFor(() => expect(mocks.assign).toHaveBeenCalledWith('/ar'));
  });
});

const overview: SecurityOverview = {
  passwordChangedAt: '2026-09-29T12:00:00Z',
  totpEnabled: false,
  recoveryCodesRemaining: 8,
  sessions: [
    { id: 's2', deviceLabel: 'متصفح كمبيوتر', current: false, lastActiveAt: '2026-10-18T12:00:00Z' },
    { id: 's1', deviceLabel: 'جوال', current: true, lastActiveAt: '2026-10-20T12:00:00Z' },
  ],
};
const nowIso = '2026-10-20T12:00:00Z';

describe('SecurityPanel', () => {
  it('summarises password, authenticator, recovery codes and devices (Arabic)', () => {
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'ar');
    expect(screen.getByText('غُيّرت قبل 3 أسابيع')).toBeInTheDocument();
    expect(screen.getByText('غير مفعّل')).toBeInTheDocument();
    expect(screen.getByText('8 من 10 متبقية')).toBeInTheDocument();
    expect(screen.getByText('هذا الجهاز')).toBeInTheDocument();
    expect(screen.getByText('جوال · نشط الآن')).toBeInTheDocument();
    expect(screen.getByText('آخر نشاط قبل يومين')).toBeInTheDocument();
    expect(screen.queryByText(/مفاتيح المرور/)).toBeNull();
  });

  it('sets up the authenticator: shows the grouped secret and link, then confirms a 6-digit code', async () => {
    mocks.api.mockResolvedValueOnce({ secret: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/Tamaneen:P-1?secret=JBSWY3DPEHPK3PXP' }).mockResolvedValueOnce(undefined);
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'إدارة تطبيق المصادقة' }));
    await userEvent.click(screen.getByRole('button', { name: 'بدء الإعداد' }));
    expect(mocks.api).toHaveBeenCalledWith('me/totp/setup', { method: 'POST' });
    expect(await screen.findByTestId('totp-secret')).toHaveTextContent('JBSW Y3DP EHPK 3PXP');
    expect(screen.getByRole('link', { name: 'فتح في تطبيق المصادقة' })).toHaveAttribute('href', 'otpauth://totp/Tamaneen:P-1?secret=JBSWY3DPEHPK3PXP');

    const code = screen.getByRole('textbox', { name: 'الرمز المكوَّن من 6 أرقام' });
    await userEvent.type(code, '12345');
    await userEvent.click(screen.getByRole('button', { name: 'تأكيد' }));
    expect(screen.getByRole('alert')).toHaveTextContent('أدخل 6 أرقام.');
    expect(mocks.api).toHaveBeenCalledTimes(1);

    await userEvent.type(code, '٦');
    await userEvent.click(screen.getByRole('button', { name: 'تأكيد' }));
    expect(mocks.api).toHaveBeenLastCalledWith('me/totp/confirm', { method: 'POST', body: { code: '123456' } });
    expect(await screen.findByText('مفعّل')).toBeInTheDocument();
    expect(screen.getByText('فعّلنا تطبيق المصادقة.')).toBeInTheDocument();
  });

  it('turns the authenticator off only with the password and a current code', async () => {
    mocks.api.mockRejectedValueOnce(new ApiError(400, { title: 'Bad', status: 400, detail: 'The current password is not correct.', code: 'error.auth.password_mismatch' }));
    renderWithIntl(<SecurityPanel overview={{ ...overview, totpEnabled: true }} nowIso={nowIso} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Manage authenticator app' }));
    await userEvent.type(screen.getByRole('textbox', { name: '6-digit code' }), '654321');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Enter your password.');
    expect(mocks.api).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));
    expect(mocks.api).toHaveBeenCalledWith('me/totp', { method: 'DELETE', body: { password: 'wrong-password', code: '654321' } });
    expect(await screen.findByText('The current password is not correct.')).toBeInTheDocument();

    mocks.api.mockResolvedValue(undefined);
    await userEvent.clear(screen.getByLabelText('Password'));
    await userEvent.type(screen.getByLabelText('Password'), 'right-password');
    await userEvent.click(screen.getByRole('button', { name: 'Turn off' }));
    expect(mocks.api).toHaveBeenLastCalledWith('me/totp', { method: 'DELETE', body: { password: 'right-password', code: '654321' } });
    expect(await screen.findByText('Off')).toBeInTheDocument();
  });

  it('ends another session and removes it from the list', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'إنهاء الجلسة على متصفح كمبيوتر' }));
    const dialog = screen.getByRole('dialog', { name: 'إنهاء الجلسة على متصفح كمبيوتر؟' });
    expect(within(dialog).getByRole('button', { name: 'إلغاء' })).toHaveFocus();
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'إنهاء الجلسة' }));
    expect(mocks.api).toHaveBeenCalledWith('me/sessions/s2', { method: 'DELETE' });
    await waitFor(() => expect(screen.queryByText('متصفح كمبيوتر')).toBeNull());
    expect(screen.getByText('هذا الجهاز')).toBeInTheDocument();
  });

  it('signs out of all devices, then goes to the login page', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الخروج من كل الأجهزة' }));
    const dialog = screen.getByRole('dialog', { name: 'تسجيل الخروج من كل الأجهزة؟' });
    await userEvent.click(within(dialog).getByRole('button', { name: 'إلغاء' }));
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الخروج من كل الأجهزة' }));
    await userEvent.click(screen.getByRole('button', { name: 'نعم، سجّل الخروج من الكل' }));
    expect(mocks.api).toHaveBeenCalledWith('me/sessions/revoke-all', { method: 'POST' });
    await waitFor(() => expect(mocks.assign).toHaveBeenCalledWith('/ar/login'));
    expect(mocks.authApi).toHaveBeenCalledWith('logout');
  });

  it('creates new recovery codes after confirming and shows them once', async () => {
    const codes = Array.from({ length: 10 }, (_, i) => `AAAA-${String(i).padStart(4, '0')}`);
    mocks.api.mockResolvedValue({ recoveryCodes: codes });
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء رموز استرجاع جديدة' }));
    const dialog = screen.getByRole('dialog', { name: 'إنشاء رموز جديدة؟' });
    expect(within(dialog).getByRole('button', { name: 'إلغاء' })).toHaveFocus();
    await userEvent.click(within(dialog).getByRole('button', { name: 'إنشاء الرموز' }));
    expect(within(dialog).getByRole('alert')).toHaveTextContent('أدخل كلمة المرور.');
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.type(within(dialog).getByLabelText('كلمة المرور'), 'my-password-1');
    await userEvent.click(within(dialog).getByRole('button', { name: 'إنشاء الرموز' }));
    expect(mocks.api).toHaveBeenCalledWith('me/recovery-codes', { method: 'POST', body: { password: 'my-password-1' } });
    expect(await screen.findByTestId('recovery-codes')).toHaveTextContent('AAAA-0009');
    expect(screen.getByText('10 من 10 متبقية')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'حفظتها' }));
    expect(screen.queryByTestId('recovery-codes')).toBeNull();
  });

  it('asks for the authenticator code too when it is on, and keeps the dialog open on a wrong code', async () => {
    mocks.api.mockRejectedValueOnce(new ApiError(400, { title: 'Bad', status: 400, detail: 'The verification code is not correct.', code: 'error.auth.mfa_failed' }));
    renderWithIntl(<SecurityPanel overview={{ ...overview, totpEnabled: true }} nowIso={nowIso} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Create new recovery codes' }));
    const dialog = screen.getByRole('dialog', { name: 'Create new codes?' });
    await userEvent.type(within(dialog).getByLabelText('Password'), 'my-password-1');
    await userEvent.type(within(dialog).getByRole('textbox', { name: '6-digit code' }), '111222');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Create codes' }));
    expect(mocks.api).toHaveBeenCalledWith('me/recovery-codes', { method: 'POST', body: { password: 'my-password-1', code: '111222' } });
    expect(await within(dialog).findByText('The verification code is not correct.')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Create new codes?' })).toBeInTheDocument();
  });

  it('validates the new password length before calling the API', async () => {
    renderWithIntl(<SecurityPanel overview={overview} nowIso={nowIso} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Change password' }));
    await userEvent.type(screen.getByLabelText('Current password'), 'old-password-1');
    await userEvent.type(screen.getByLabelText('New password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Save password' }));
    expect(screen.getByRole('alert')).toHaveTextContent('at least 12 characters');
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.type(screen.getByLabelText('New password'), '-and-longer');
    mocks.api.mockResolvedValue(undefined);
    await userEvent.click(screen.getByRole('button', { name: 'Save password' }));
    expect(mocks.api).toHaveBeenCalledWith('me/password', { method: 'PUT', body: { currentPassword: 'old-password-1', newPassword: 'short-and-longer' } });
    expect(await screen.findByText('Your password is changed.')).toBeInTheDocument();
  });
});

const prefs: NotificationPrefs = {
  push: true,
  sms: true,
  email: false,
  neutralText: true,
  quickExit: false,
  homeworkReminder: 'daily',
  reminderTime: '21:00',
  quietFrom: '23:00',
  quietTo: '08:00',
};

describe('NotificationsForm and QuickExit', () => {
  it('shows the design copy with Latin-digit times (Arabic)', () => {
    renderWithIntl(<NotificationsForm initial={prefs} />, 'ar');
    expect(screen.getByRole('switch', { name: 'إشعارات التطبيق' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('switch', { name: 'البريد الإلكتروني' })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByText('من 11:00 م إلى 8:00 ص')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'مرة يوميًا' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByText('اتصالات فريق الطوارئ تصلك دائمًا، حتى في ساعات الهدوء.')).toBeInTheDocument();
  });

  it('saves the preferences and sets the quick-exit cookie, then clears it when turned off', async () => {
    mocks.api.mockImplementation(async (_path: string, { body }: { body: NotificationPrefs }) => body);
    renderWithIntl(<NotificationsForm initial={prefs} />, 'en');
    await userEvent.click(screen.getByRole('switch', { name: 'Quick exit button' }));
    await userEvent.click(screen.getByRole('radio', { name: 'Every two days' }));
    fireEvent.change(screen.getByLabelText('Preferred time'), { target: { value: '20:30' } });
    await userEvent.click(screen.getByRole('button', { name: 'Edit quiet hours' }));
    fireEvent.change(screen.getByLabelText('From'), { target: { value: '22:00' } });
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(mocks.api).toHaveBeenCalledWith('me/notifications', {
      method: 'PUT',
      body: { ...prefs, quickExit: true, homeworkReminder: 'every2days', reminderTime: '20:30', quietFrom: '22:00' },
    });
    expect(await screen.findByText('Your notification settings are saved.')).toBeInTheDocument();
    expect(document.cookie).toContain('tamaneen_qe=1');

    await userEvent.click(screen.getByRole('switch', { name: 'Quick exit button' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(document.cookie).not.toContain('tamaneen_qe=1'));
  });

  it('follows the saved preference on load', () => {
    renderWithIntl(<NotificationsForm initial={{ ...prefs, quickExit: true }} />, 'ar');
    expect(document.cookie).toContain('tamaneen_qe=1');
  });

  it('shows the quick-exit button only while the cookie is set, and leaves for a neutral site', async () => {
    const replaceState = vi.spyOn(window.history, 'replaceState');
    renderWithIntl(<QuickExit />, 'ar');
    expect(screen.queryByTestId('quick-exit')).toBeNull();
    renderWithIntl(<NotificationsForm initial={{ ...prefs, quickExit: true }} />, 'ar');
    const button = await screen.findByRole('button', { name: 'خروج سريع: ينقلك فورًا إلى صفحة عادية' });
    await userEvent.click(button);
    expect(replaceState).toHaveBeenCalled();
    expect(mocks.locationReplace).toHaveBeenCalledWith('https://www.google.com');
    replaceState.mockRestore();
  });
});

const safety: SafetyInfo = { contactPhone: '0500004521', trustedName: 'أختي', trustedPhone: '0511111111', shareLocation: true, shareLocationSince: '2026-10-19T09:00:00Z' };

describe('SafetyForm', () => {
  it('shows the fields, sharing date, and saves with ASCII digits', async () => {
    mocks.api.mockImplementation(async (_p: string, { body }: { body: SafetyInfo }) => ({ ...body, shareLocationSince: safety.shareLocationSince }));
    renderWithIntl(<SafetyForm initial={safety} timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByText('مفعّلة منذ 19 أكتوبر')).toBeInTheDocument();
    const phone = screen.getByRole('textbox', { name: 'رقم جواله' });
    expect(phone).toHaveAttribute('dir', 'ltr');
    await userEvent.clear(phone);
    await userEvent.type(phone, '٠٥٢٢٢٢٢٢٢٢');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    expect(mocks.api).toHaveBeenCalledWith('me/safety-info', { method: 'PUT', body: { contactPhone: '0500004521', trustedName: 'أختي', trustedPhone: '0522222222', shareLocation: true } });
    expect(await screen.findByText('حفظنا بيانات الطوارئ.')).toBeInTheDocument();
  });

  it('asks before deleting the trusted contact', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<SafetyForm initial={safety} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Delete emergency contact' }));
    const dialog = screen.getByRole('dialog', { name: 'Delete your emergency contact?' });
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.click(within(dialog).getByRole('button', { name: 'Delete' }));
    expect(mocks.api).toHaveBeenCalledWith('me/safety-info/trusted-contact', { method: 'DELETE' });
    await waitFor(() => expect(screen.getByRole('textbox', { name: 'Someone you trust' })).toHaveValue(''));
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('keeps the trusted name and number together', async () => {
    renderWithIntl(<SafetyForm initial={safety} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.clear(screen.getByRole('textbox', { name: 'Their mobile number' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    expect(screen.getByText('Add the mobile number of the person you trust, or remove their name.')).toBeInTheDocument();
    expect(mocks.api).not.toHaveBeenCalled();
  });

  it('asks before turning location sharing off (Arabic)', async () => {
    mocks.api.mockImplementation(async (_p: string, { body }: { body: SafetyInfo }) => ({ ...body, shareLocationSince: null }));
    renderWithIntl(<SafetyForm initial={safety} timeZone="Asia/Riyadh" />, 'ar');
    const sw = screen.getByRole('switch', { name: /مشاركة موقعي عند الطوارئ/ });
    await userEvent.click(sw);
    const dialog = screen.getByRole('dialog', { name: 'إيقاف مشاركة موقعك عند الطوارئ؟' });
    expect(sw).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(within(dialog).getByRole('button', { name: 'إيقاف المشاركة' }));
    expect(sw).toHaveAttribute('aria-checked', 'false');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    expect(mocks.api).toHaveBeenCalledWith('me/safety-info', { method: 'PUT', body: { contactPhone: '0500004521', trustedName: 'أختي', trustedPhone: '0511111111', shareLocation: false } });
  });

  it('closes a dialog with Escape', async () => {
    renderWithIntl(<SafetyForm initial={safety} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Delete emergency contact' }));
    act(() => {
      fireEvent.keyDown(document, { key: 'Escape' });
    });
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});
