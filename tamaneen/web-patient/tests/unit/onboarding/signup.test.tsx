import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { apiError, resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';
import { SignupForm } from '@/components/onboarding/SignupForm';

const apiMock = vi.mocked(api);

function withToken() {
  window.sessionStorage.setItem(FLOW_KEYS.eligibility, JSON.stringify({ eligibilityToken: 'elig-1' }));
}

describe('P02 Signup', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('sends the visitor back to eligibility when the token is missing', async () => {
    renderWithIntl(<SignupForm />, 'ar');
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/start/eligibility?restart=1'));
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('suggests a nickname and offers a new one (Arabic)', async () => {
    withToken();
    apiMock.mockResolvedValueOnce({ nickname: 'نخلة هادئة' }).mockResolvedValueOnce({ nickname: 'غيمة صافية' });
    renderWithIntl(<SignupForm />, 'ar');
    const nickname = screen.getByLabelText('الاسم المستعار');
    await waitFor(() => expect(nickname).toHaveValue('نخلة هادئة'));
    await userEvent.click(screen.getByRole('button', { name: 'اسم جديد' }));
    await waitFor(() => expect(nickname).toHaveValue('غيمة صافية'));
    expect(apiMock).toHaveBeenCalledWith('auth/nickname');
    expect(screen.getByRole('radio', { name: 'رقم الجوال' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByLabelText('رقم الجوال')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByText('سنرسل رمز تحقق من 6 أرقام. لن نشاركه مع أحد.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'تسجيل الدخول' })).toHaveAttribute('href', '/login');
  });

  it('validates the contact and password before submitting (English)', async () => {
    withToken();
    apiMock.mockResolvedValueOnce({ nickname: 'Calm Palm' });
    renderWithIntl(<SignupForm />, 'en');
    await waitFor(() => expect(screen.getByLabelText('Nickname')).toHaveValue('Calm Palm'));
    await userEvent.type(screen.getByLabelText('Mobile number'), '123');
    await userEvent.type(screen.getByLabelText('Password'), 'short');
    expect(screen.getByText('Too short: at least 12 characters, and not a leaked password')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(screen.getByText('Enter a valid mobile number starting with 05 or your country code.')).toBeInTheDocument();
    expect(screen.getByText('Your password needs at least 12 characters.')).toBeInTheDocument();
    expect(apiMock).toHaveBeenCalledTimes(1);

    await userEvent.click(screen.getByRole('radio', { name: 'Email' }));
    await userEvent.type(screen.getByLabelText('Email'), 'not-an-email');
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(screen.getByText('Enter a valid email address.')).toBeInTheDocument();
  });

  it('converts Arabic-Indic digits, signs up and goes to verify with the challenge in sessionStorage', async () => {
    withToken();
    apiMock.mockResolvedValueOnce({ nickname: 'نخلة هادئة' });
    apiMock.mockResolvedValueOnce({ challengeId: 'ch-1', maskedContact: '•••• 4521', resendAfterSeconds: 60, expiresInSeconds: 600 });
    renderWithIntl(<SignupForm />, 'ar');
    await waitFor(() => expect(screen.getByLabelText('الاسم المستعار')).toHaveValue('نخلة هادئة'));
    await userEvent.type(screen.getByLabelText('رقم الجوال'), '٠٥٥ ١٢٣ ٤٥٢١');
    await userEvent.type(screen.getByLabelText('كلمة المرور'), 'a-long-passphrase');
    expect(screen.getByText('قوية: 12 حرفًا على الأقل، وليست من كلمات المرور المسربة')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إنشاء الحساب' }));

    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/verify'));
    expect(apiMock).toHaveBeenLastCalledWith('auth/signup', {
      method: 'POST',
      body: { eligibilityToken: 'elig-1', nickname: 'نخلة هادئة', contactType: 'phone', contact: '0551234521', password: 'a-long-passphrase', language: 'ar' },
    });
    const saved = JSON.parse(window.sessionStorage.getItem(FLOW_KEYS.signup)!);
    expect(saved).toMatchObject({ challengeId: 'ch-1', maskedContact: '•••• 4521', resendAfterSeconds: 60, contactType: 'phone' });
    expect(saved).not.toHaveProperty('password');
  });

  it('shows field errors returned by the API', async () => {
    withToken();
    apiMock.mockResolvedValueOnce({ nickname: 'Calm Palm' });
    apiMock.mockRejectedValueOnce(apiError(400, { title: 'Validation failed', errors: { Password: ['This password appeared in a data breach.'] } }));
    renderWithIntl(<SignupForm />, 'en');
    await waitFor(() => expect(screen.getByLabelText('Nickname')).toHaveValue('Calm Palm'));
    await userEvent.type(screen.getByLabelText('Mobile number'), '0551234521');
    await userEvent.type(screen.getByLabelText('Password'), 'password123456');
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }));
    expect(await screen.findByText('This password appeared in a data breach.')).toBeInTheDocument();
    expect(screen.getByLabelText('Password')).toHaveAttribute('aria-invalid', 'true');
    expect(router.push).not.toHaveBeenCalled();
  });

  it('shows and hides the password', async () => {
    withToken();
    apiMock.mockResolvedValueOnce({ nickname: 'Calm Palm' });
    renderWithIntl(<SignupForm />, 'en');
    const input = screen.getByLabelText('Password');
    expect(input).toHaveAttribute('type', 'password');
    await userEvent.click(screen.getByRole('button', { name: 'Show password' }));
    expect(input).toHaveAttribute('type', 'text');
    await userEvent.click(screen.getByRole('button', { name: 'Hide password' }));
    expect(input).toHaveAttribute('type', 'password');
  });
});
