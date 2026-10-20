import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { resetAll, router } from '../onboarding/helpers';

vi.mock('@/i18n/navigation', async () => (await import('../onboarding/helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('../onboarding/helpers')).clientMock());

import { api } from '@/lib/api/client';
import { RecoverForm } from '@/components/auth/RecoverForm';
import { isValidContact, normalizeContact, normalizeRecoveryCode, pluralKey } from '@/components/auth/contact';

const apiMock = vi.mocked(api);
const NEUTRAL_AR = 'إن كان الحساب موجودًا فسيصلك رمز خلال دقيقة. لأمانك، نعرض الرسالة نفسها في كل الأحوال.';

describe('P20 Recover', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('always shows the same neutral confirmation, then resets the password with the code (Arabic)', async () => {
    apiMock.mockResolvedValueOnce({ challengeId: 'rc-1', maskedContact: '••21', resendAfterSeconds: 60, expiresInSeconds: 600 }).mockResolvedValueOnce(undefined);
    renderWithIntl(<RecoverForm />, 'ar');
    expect(screen.getByRole('radio', { name: /برمز على جوالي أو بريدي/ })).toBeChecked();
    expect(screen.getByText(/فقدت الجوال ورموز الاسترجاع معًا؟/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('رقم الجوال أو البريد'), '0551234521');
    await userEvent.click(screen.getByRole('button', { name: 'إرسال الرمز' }));

    expect(await screen.findByRole('status')).toHaveTextContent(NEUTRAL_AR);
    expect(apiMock).toHaveBeenCalledWith('auth/recover', { method: 'POST', body: { contact: '0551234521' } });
    expect(screen.getByRole('heading', { level: 1, name: 'استرجاع حسابك' })).toHaveFocus();

    await userEvent.type(screen.getByLabelText('الرمز المرسل إليك'), '123456');
    await userEvent.type(screen.getByLabelText('كلمة المرور الجديدة'), 'a-brand-new-passphrase');
    await userEvent.click(screen.getByRole('button', { name: 'تعيين كلمة المرور' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/login?reset=1'));
    expect(apiMock).toHaveBeenLastCalledWith('auth/recover/complete', { method: 'POST', body: { challengeId: 'rc-1', code: '123456', newPassword: 'a-brand-new-passphrase' } });
  });

  it('resets with a recovery code (English)', async () => {
    apiMock.mockResolvedValueOnce(undefined);
    renderWithIntl(<RecoverForm />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: /With a recovery code/ }));
    await userEvent.type(screen.getByLabelText('Mobile number or email'), 'User@Example.com');
    await userEvent.type(screen.getByLabelText('Recovery code'), 'p4tz-81ld');
    await userEvent.type(screen.getByLabelText('New password'), 'short');
    await userEvent.click(screen.getByRole('button', { name: 'Set new password' }));
    expect(screen.getByText('Your password needs at least 12 characters.')).toBeInTheDocument();
    expect(apiMock).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText('New password'), '-but-longer-now');
    await userEvent.click(screen.getByRole('button', { name: 'Set new password' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/login?reset=1'));
    expect(apiMock).toHaveBeenCalledWith('auth/recover/code', { method: 'POST', body: { contact: 'user@example.com', recoveryCode: 'P4TZ-81LD', newPassword: 'short-but-longer-now' } });
  });

  it('normalizes contacts, recovery codes and plural keys', () => {
    expect(normalizeContact(' ٠٥٥ ١٢٣-٤٥٢١ ')).toBe('0551234521');
    expect(isValidContact('not a contact')).toBe(false);
    expect(normalizeRecoveryCode('h1md 7tpk')).toBe('H1MD7TPK');
    expect(pluralKey('ar', 2)).toBe('two');
    expect(pluralKey('en', 2)).toBe('other');
  });
});
