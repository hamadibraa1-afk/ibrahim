import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { apiError, resetAll, router } from '../onboarding/helpers';

vi.mock('@/i18n/navigation', async () => (await import('../onboarding/helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('../onboarding/helpers')).clientMock());

import { api, authApi } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { LoginForm } from '@/components/auth/LoginForm';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';

const apiMock = vi.mocked(api);
const authMock = vi.mocked(authApi);

const me: Me = {
  pseudonymId: 'P-1',
  nickname: 'نخلة',
  language: 'ar',
  timeZone: 'Asia/Riyadh',
  onboarding: { consentsDone: true, safetyInfoDone: false, path: null, screeningDone: false, intakeStatus: 'none', triageStatus: 'none' },
  chatLocked: false,
};

async function fill(contact: string, password: string, submit: string, contactLabel: string, passwordLabel: string) {
  await userEvent.type(screen.getByLabelText(contactLabel), contact);
  await userEvent.type(screen.getByLabelText(passwordLabel), password);
  await userEvent.click(screen.getByRole('button', { name: submit }));
}

describe('P18 Login', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
    authMock.mockReset();
  });

  it('shows the API problem and the attempts left before the pause (Arabic)', async () => {
    authMock.mockRejectedValueOnce(apiError(401, { title: 'البيانات غير صحيحة.', remainingAttempts: 2 }));
    renderWithIntl(<LoginForm />, 'ar');
    expect(screen.getByRole('link', { name: 'نسيت كلمة المرور؟' })).toHaveAttribute('href', '/recover');
    expect(screen.getByRole('link', { name: 'ابدأ الآن' })).toHaveAttribute('href', '/start/eligibility');
    expect(screen.queryByText(/مفتاح المرور/)).not.toBeInTheDocument();
    await fill('٠٥٥١٢٣٤٥٢١', 'wrong-password', 'تسجيل الدخول', 'رقم الجوال أو البريد', 'كلمة المرور');
    const alert = await screen.findByTestId('login-problem');
    expect(alert).toHaveAttribute('role', 'alert');
    expect(alert).toHaveTextContent('البيانات غير صحيحة. تبقّت محاولتان قبل الإيقاف المؤقت 15 دقيقة.');
    expect(authMock).toHaveBeenCalledWith('login', { contact: '0551234521', password: 'wrong-password' });
  });

  it('explains the attempts left in English', async () => {
    authMock.mockRejectedValueOnce(apiError(401, { title: 'Incorrect details.', remainingAttempts: 2 }));
    renderWithIntl(<LoginForm />, 'en');
    await fill('user@example.com', 'wrong-password', 'Sign in', 'Mobile number or email', 'Password');
    expect(await screen.findByTestId('login-problem')).toHaveTextContent('Incorrect details. 2 attempts left before a 15-minute pause.');
  });

  it('shows the lock with the minutes from retryAfterSeconds on 423', async () => {
    authMock.mockRejectedValueOnce(apiError(423, { title: 'Locked', retryAfterSeconds: 600 }));
    renderWithIntl(<LoginForm />, 'en');
    await fill('user@example.com', 'secret-password', 'Sign in', 'Mobile number or email', 'Password');
    expect(await screen.findByTestId('login-problem')).toHaveTextContent("We've paused sign-in to protect your account. Try again in 10 minutes.");
  });

  it('keeps the MFA step in sessionStorage and moves to /login/mfa', async () => {
    authMock.mockResolvedValueOnce({ status: 'mfa', mfaToken: 'mfa-1', methods: ['totp', 'sms', 'recovery'], maskedPhone: '••21' });
    renderWithIntl(<LoginForm />, 'en');
    await fill('User@Example.com', 'secret-password', 'Sign in', 'Mobile number or email', 'Password');
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/login/mfa'));
    expect(authMock).toHaveBeenCalledWith('login', { contact: 'user@example.com', password: 'secret-password' });
    expect(JSON.parse(window.sessionStorage.getItem(FLOW_KEYS.mfa)!)).toEqual({ mfaToken: 'mfa-1', methods: ['totp', 'sms', 'recovery'], maskedPhone: '••21' });
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('lands on the next onboarding step after a plain sign-in', async () => {
    authMock.mockResolvedValueOnce({ status: 'ok' });
    apiMock.mockResolvedValueOnce(me);
    renderWithIntl(<LoginForm />, 'en');
    await fill('0551234521', 'secret-password', 'Sign in', 'Mobile number or email', 'Password');
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/start/safety-info'));
    expect(apiMock).toHaveBeenCalledWith('me');
  });

  it('shows the reset notice and required-field errors', async () => {
    renderWithIntl(<LoginForm notice="reset" />, 'ar');
    expect(screen.getByRole('status')).toHaveTextContent('تم تغيير كلمة المرور. سجّل الدخول بكلمة المرور الجديدة.');
    await userEvent.click(screen.getByRole('button', { name: 'تسجيل الدخول' }));
    expect(screen.getByText('اكتب رقم جوالك أو بريدك.')).toBeInTheDocument();
    expect(screen.getByText('اكتب كلمة المرور.')).toBeInTheDocument();
    expect(authMock).not.toHaveBeenCalled();
  });
});
