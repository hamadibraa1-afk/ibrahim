import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithIntl } from '../intl';
import { apiError, resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api, authApi } from '@/lib/api/client';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';
import { VerifyForm } from '@/components/onboarding/VerifyForm';

const apiMock = vi.mocked(api);
const authMock = vi.mocked(authApi);

function saveChallenge(resendAfterSeconds = 42, contactType: 'phone' | 'email' = 'phone') {
  window.sessionStorage.setItem(
    FLOW_KEYS.signup,
    JSON.stringify({ nickname: 'نخلة', contactType, contact: '0551234521', challengeId: 'ch-1', maskedContact: '•••• 4521', resendAfterSeconds, sentAt: Date.now() }),
  );
}

async function flush() {
  await act(async () => {
    await Promise.resolve();
  });
}

describe('P16 Verify', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
    authMock.mockReset();
    vi.useFakeTimers({ toFake: ['setInterval', 'clearInterval', 'setTimeout', 'clearTimeout', 'Date'] });
  });
  afterEach(() => vi.useRealTimers());

  it('returns to signup with a notice when the challenge is missing', async () => {
    renderWithIntl(<VerifyForm />, 'ar');
    await flush();
    expect(router.replace).toHaveBeenCalledWith('/start/signup?restart=1');
  });

  it('shows the masked number and counts down to the resend button (Arabic)', async () => {
    saveChallenge(42);
    renderWithIntl(<VerifyForm />, 'ar');
    await flush();
    expect(screen.getByRole('heading', { level: 1, name: 'أدخل رمز التحقق' })).toBeInTheDocument();
    expect(screen.getByText('•••• 4521').tagName).toBe('BDI');
    expect(screen.getByRole('timer')).toHaveTextContent('0:42');
    expect(screen.getByRole('button', { name: 'إعادة إرسال الرمز' })).toBeDisabled();
    expect(screen.getByLabelText('رمز التحقق')).toHaveAttribute('autocomplete', 'one-time-code');
    expect(screen.getByLabelText('رمز التحقق')).toHaveAttribute('inputmode', 'numeric');
    expect(screen.getByRole('link', { name: 'تغيير رقم الجوال' })).toHaveAttribute('href', '/start/signup');

    await act(async () => {
      vi.advanceTimersByTime(2000);
    });
    expect(screen.getByRole('timer')).toHaveTextContent('0:40');

    await act(async () => {
      vi.advanceTimersByTime(40_000);
    });
    expect(screen.queryByRole('timer')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إعادة إرسال الرمز' })).toBeEnabled();
    expect(screen.getAllByText('لم يصلك الرمز؟ يمكنك طلب رمز جديد الآن.').length).toBeGreaterThan(0);
  });

  it('restarts the countdown from retryAfterSeconds when resend answers 429 (English)', async () => {
    saveChallenge(0);
    apiMock.mockRejectedValueOnce(apiError(429, { title: 'Too many requests', retryAfterSeconds: 30 }));
    renderWithIntl(<VerifyForm />, 'en');
    await flush();
    const resend = screen.getByRole('button', { name: 'Resend code' });
    expect(resend).toBeEnabled();
    await act(async () => {
      fireEvent.click(resend);
    });
    await flush();
    expect(apiMock).toHaveBeenCalledWith('auth/challenges/ch-1/resend', { method: 'POST' });
    expect(screen.getByRole('timer')).toHaveTextContent('0:30');
    expect(screen.getByRole('button', { name: 'Resend code' })).toBeDisabled();
  });

  it('resends a code and confirms it politely', async () => {
    saveChallenge(0);
    apiMock.mockResolvedValueOnce({ challengeId: 'ch-2', maskedContact: '•••• 4521', resendAfterSeconds: 60, expiresInSeconds: 600 });
    renderWithIntl(<VerifyForm />, 'en');
    await flush();
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Resend code' }));
    });
    await flush();
    expect(screen.getAllByText('We sent a new code.').length).toBeGreaterThan(0);
    expect(screen.getByRole('timer')).toHaveTextContent('1:00');
    expect(JSON.parse(window.sessionStorage.getItem(FLOW_KEYS.signup)!).challengeId).toBe('ch-2');
  });

  it('verifies the code, keeps the recovery codes for P17 and clears the signup state', async () => {
    saveChallenge(60);
    window.sessionStorage.setItem(FLOW_KEYS.eligibility, JSON.stringify({ eligibilityToken: 'e' }));
    authMock.mockResolvedValueOnce({ recoveryCodes: ['7KQ2-M9XA', 'P4TZ-81LD'] });
    renderWithIntl(<VerifyForm />, 'en');
    await flush();
    fireEvent.change(screen.getByLabelText('Verification code'), { target: { value: '٤٨٢١٣٣' } });
    expect(screen.getByLabelText('Verification code')).toHaveValue('482133');
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Verify' }));
    });
    await flush();
    expect(authMock).toHaveBeenCalledWith('signup-verify', { challengeId: 'ch-1', code: '482133' });
    expect(router.replace).toHaveBeenCalledWith('/start/recovery-codes');
    expect(JSON.parse(window.sessionStorage.getItem(FLOW_KEYS.recoveryCodes)!)).toEqual(['7KQ2-M9XA', 'P4TZ-81LD']);
    expect(window.sessionStorage.getItem(FLOW_KEYS.signup)).toBeNull();
    expect(window.sessionStorage.getItem(FLOW_KEYS.eligibility)).toBeNull();
  });

  it('shows the API problem for a wrong code', async () => {
    saveChallenge(60, 'email');
    authMock.mockRejectedValueOnce(apiError(400, { title: 'الرمز غير صحيح' }));
    renderWithIntl(<VerifyForm />, 'ar');
    await flush();
    expect(screen.getByRole('link', { name: 'تغيير البريد الإلكتروني' })).toBeInTheDocument();
    fireEvent.change(screen.getByLabelText('رمز التحقق'), { target: { value: '111111' } });
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'تحقق' }));
    });
    await flush();
    expect(screen.getByRole('alert')).toHaveTextContent('الرمز غير صحيح');
  });
});
