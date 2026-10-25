import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { apiError, resetAll, router } from '../onboarding/helpers';

vi.mock('@/i18n/navigation', async () => (await import('../onboarding/helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('../onboarding/helpers')).clientMock());

import { api, authApi } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { MfaForm } from '@/components/auth/MfaForm';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';

const apiMock = vi.mocked(api);
const authMock = vi.mocked(authApi);
const done: Me = {
  pseudonymId: 'P-1',
  nickname: 'n',
  language: 'en',
  timeZone: 'Asia/Riyadh',
  onboarding: { consentsDone: true, safetyInfoDone: true, path: 'direct', screeningDone: true, intakeStatus: 'none', triageStatus: 'none' },
  chatLocked: false,
};

function saveMfa(methods: string[] = ['totp', 'sms', 'recovery']) {
  window.sessionStorage.setItem(FLOW_KEYS.mfa, JSON.stringify({ mfaToken: 'mfa-1', methods, maskedPhone: '••21' }));
}

describe('P19 Login MFA', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
    authMock.mockReset();
  });

  it('returns to login when the MFA step is missing', async () => {
    renderWithIntl(<MfaForm />, 'en');
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/login?restart=1'));
  });

  it('confirms the authenticator code with "remember this device" (Arabic)', async () => {
    saveMfa();
    authMock.mockResolvedValueOnce({ status: 'ok' });
    apiMock.mockResolvedValueOnce(done);
    renderWithIntl(<MfaForm />, 'ar');
    expect(await screen.findByRole('heading', { level: 1, name: 'تأكيد الدخول' })).toBeInTheDocument();
    expect(screen.getByText('أدخل الرمز من تطبيق المصادقة على جوالك.')).toBeInTheDocument();
    expect(screen.queryByText(/مفتاح المرور/)).not.toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('رمز التحقق'), '482100');
    await userEvent.click(screen.getByLabelText('تذكر هذا الجهاز 30 يومًا'));
    await userEvent.click(screen.getByRole('button', { name: 'تأكيد' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/home'));
    expect(authMock).toHaveBeenCalledWith('login-mfa', { mfaToken: 'mfa-1', method: 'totp', code: '482100', rememberDevice: true });
    expect(window.sessionStorage.getItem(FLOW_KEYS.mfa)).toBeNull();
  });

  it('uses a recovery code as the method (English)', async () => {
    saveMfa();
    authMock.mockResolvedValueOnce({ status: 'ok' });
    apiMock.mockResolvedValueOnce(done);
    renderWithIntl(<MfaForm />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: 'Use a recovery code' }));
    const heading = screen.getByRole('heading', { level: 1, name: "Confirm it's you" });
    expect(heading).toHaveFocus();
    expect(screen.getByText(/Enter one of the recovery codes you saved/)).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Recovery code'), '7kq2-m9xa');
    expect(screen.getByLabelText('Recovery code')).toHaveValue('7KQ2-M9XA');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    await waitFor(() => expect(authMock).toHaveBeenCalledWith('login-mfa', { mfaToken: 'mfa-1', method: 'recovery', code: '7KQ2-M9XA', rememberDevice: false }));
    expect(screen.getByRole('button', { name: 'Use the authenticator app' })).toBeInTheDocument();
  });

  it('sends an SMS code and switches to the sms method', async () => {
    saveMfa();
    apiMock.mockResolvedValueOnce(undefined);
    authMock.mockRejectedValueOnce(apiError(400, { title: 'The code is not correct.' }));
    renderWithIntl(<MfaForm />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: /Send a code by text message/ }));
    expect(apiMock).toHaveBeenCalledWith('auth/login/mfa/sms', { method: 'POST', body: { mfaToken: 'mfa-1' } });
    expect(await screen.findByText('We sent a text message with a code to', { exact: false })).toBeInTheDocument();
    await userEvent.type(screen.getByLabelText('Verification code'), '111111');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('The code is not correct.');
    expect(authMock).toHaveBeenCalledWith('login-mfa', { mfaToken: 'mfa-1', method: 'sms', code: '111111', rememberDevice: false });
  });

  it('counts down 60 seconds after an SMS, then offers to resend it (Arabic)', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      saveMfa(['sms']);
      apiMock.mockResolvedValue(undefined);
      const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
      renderWithIntl(<MfaForm />, 'ar');
      await user.click(await screen.findByRole('button', { name: 'إرسال الرمز' }));
      const box = await screen.findByTestId('sms-resend');
      expect(box).toHaveTextContent('يمكنك طلب رمز جديد بعد');
      expect(await screen.findByRole('timer')).toHaveTextContent(/^(1:00|0:59)$/);
      const resend = screen.getByRole('button', { name: 'إعادة إرسال الرمز' });
      expect(resend).toBeDisabled();

      await act(async () => {
        vi.advanceTimersByTime(61_000);
      });
      expect(box).toHaveTextContent('يمكنك طلب رمز جديد الآن.');
      expect(resend).toBeEnabled();
      await user.click(resend);
      expect(apiMock).toHaveBeenCalledTimes(2);
      expect(await screen.findByText('أرسلنا رمزًا جديدًا.', { selector: 'p:not(.sr-only)' })).toBeInTheDocument();
      expect(await screen.findByRole('timer')).toHaveTextContent(/^(1:00|0:59)$/);
    } finally {
      vi.useRealTimers();
    }
  });
});
