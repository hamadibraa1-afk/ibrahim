'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api, ApiError, authApi } from '@/lib/api/client';
import type { MfaMethod } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { Spinner } from '@/components/ui/Spinner';
import { TextField } from '@/components/ui/TextField';
import { CheckboxRow } from '@/components/onboarding/CheckboxRow';
import { FLOW_KEYS, clearFlow, isMfaState, readFlow, type MfaState } from '@/components/onboarding/flowStorage';
import { StepHeading } from '@/components/onboarding/StepHeading';
import { CODE_LENGTH, normalizeCode, normalizeRecoveryCode } from './contact';
import { fieldErrorsOf, problemText } from './errorText';
import { signedInPath } from './finishSignIn';
import { formatSeconds, useCountdown } from './useCountdown';

/** A new SMS code can be requested after this long (docs/api-v1.md; the API answers 429 sooner). */
export const SMS_RESEND_SECONDS = 60;

/**
 * P19 Login MFA: authenticator code, or an SMS code, or a recovery code. No passkeys in this release.
 * After an SMS is sent, a 60-second countdown precedes a "resend" button.
 */
export function MfaForm() {
  const t = useTranslations('loginMfa');
  const tu = useTranslations('ui');
  const te = useTranslations('errors');
  const router = useRouter();
  const [state, setState] = useState<MfaState | null>(null);
  const [method, setMethod] = useState<MfaMethod>('totp');
  const [smsSent, setSmsSent] = useState(false);
  const [smsAgainAt, setSmsAgainAt] = useState<number | null>(null);
  const [resent, setResent] = useState(false);
  const ticking = useCountdown(smsAgainAt);
  // The hook catches up one render after a new deadline; until then count from the deadline itself.
  const smsWait = ticking > 0 || smsAgainAt === null ? ticking : Math.max(0, Math.ceil((smsAgainAt - Date.now()) / 1000));
  const [code, setCode] = useState('');
  const [remember, setRemember] = useState(false);
  const [busy, setBusy] = useState<'confirm' | 'sms' | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const saved = readFlow(FLOW_KEYS.mfa, isMfaState);
    if (!saved || saved.methods.length === 0) {
      router.replace('/login?restart=1');
      return;
    }
    setState(saved);
    setMethod(saved.methods.includes('totp') ? 'totp' : (saved.methods[0] ?? 'totp'));
  }, [router]);

  if (!state) return <Spinner label={tu('loading')} />;
  const mfa = state;

  function switchTo(next: MfaMethod) {
    setMethod(next);
    setCode('');
    setError(null);
  }

  async function sendSms() {
    const again = smsSent;
    setBusy('sms');
    setError(null);
    setResent(false);
    try {
      await api<void>('auth/login/mfa/sms', { method: 'POST', body: { mfaToken: mfa.mfaToken } });
      setSmsAgainAt(Date.now() + SMS_RESEND_SECONDS * 1000);
      setSmsSent(true);
      if (again) setResent(true);
      if (method !== 'sms') switchTo('sms');
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) {
        setSmsAgainAt(Date.now() + (e.problem.retryAfterSeconds ?? SMS_RESEND_SECONDS) * 1000);
      } else {
        setError(problemText(e, te('network')));
      }
    } finally {
      setBusy(null);
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault();
    const value = method === 'recovery' ? normalizeRecoveryCode(code) : normalizeCode(code);
    if (method === 'recovery' ? value.replace(/-/g, '').length < 8 : value.length !== CODE_LENGTH) {
      setError(method === 'recovery' ? t('recoveryInvalid') : t('codeInvalid'));
      return;
    }
    setBusy('confirm');
    setError(null);
    try {
      await authApi<{ status: 'ok' }>('login-mfa', { mfaToken: mfa.mfaToken, method, code: value, rememberDevice: remember });
      clearFlow(FLOW_KEYS.mfa);
      router.replace(await signedInPath());
    } catch (err) {
      setError(fieldErrorsOf(err).code ?? problemText(err, te('network')));
      setBusy(null);
    }
  }

  const needsSmsFirst = method === 'sms' && !smsSent;
  const phone = mfa.maskedPhone;
  const subtitle =
    method === 'totp'
      ? t('totpBody')
      : method === 'recovery'
        ? t('recoveryBody')
        : smsSent
          ? phone
            ? t.rich('smsBody', { phone, b: (chunks) => <bdi dir="ltr">{chunks}</bdi> })
            : t('smsBodyNoPhone')
          : t('smsPending');

  const others = (['sms', 'totp', 'recovery'] as const).filter((m) => m !== method && mfa.methods.includes(m));

  return (
    <div className="flex flex-col gap-5">
      <StepHeading title={t('title')} subtitle={subtitle} focusKey={method} />

      {needsSmsFirst ? (
        <Button full onClick={() => void sendSms()} busy={busy === 'sms'}>
          {t('sendSms')}
        </Button>
      ) : (
        <form onSubmit={submit} noValidate className="flex flex-col gap-5">
          {method === 'recovery' ? (
            <TextField
              key="recovery"
              label={t('recoveryCode')}
              value={code}
              onChange={(e) => {
                setCode(normalizeRecoveryCode(e.target.value));
                setError(null);
              }}
              dir="ltr"
              autoComplete="off"
              autoCapitalize="characters"
              spellCheck={false}
              maxLength={20}
              placeholder="XXXX-XXXX"
              error={error}
              className="text-center font-mono text-xl tracking-widest"
            />
          ) : (
            <TextField
              key="code"
              label={t('code')}
              value={code}
              onChange={(e) => {
                setCode(normalizeCode(e.target.value));
                setError(null);
              }}
              dir="ltr"
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="[0-9]*"
              maxLength={CODE_LENGTH}
              error={error}
              className="text-center font-mono text-2xl font-bold tracking-[0.5em]"
            />
          )}
          <CheckboxRow checked={remember} onChange={setRemember} label={t('remember')} />
          <Button type="submit" full busy={busy === 'confirm'} disabled={busy !== null}>
            {t('submit')}
          </Button>
        </form>
      )}

      {needsSmsFirst && error ? <Notice tone="danger">{error}</Notice> : null}
      {smsSent && method === 'sms' ? <p className="sr-only" aria-live="polite">{resent ? t('smsResent') : t('smsSent')}</p> : null}

      {smsSent && method === 'sms' ? (
        <div className="flex flex-col items-center gap-2 text-center" data-testid="sms-resend">
          {smsWait > 0 ? (
            <p className="text-sm text-muted">
              {t.rich('smsWait', {
                time: formatSeconds(smsWait),
                b: (chunks) => (
                  <b role="timer" dir="ltr" className="font-bold text-ink">
                    {chunks}
                  </b>
                ),
              })}
            </p>
          ) : (
            <p className="text-sm text-muted">{t('smsReady')}</p>
          )}
          <Button variant="ghost" compact onClick={() => void sendSms()} disabled={smsWait > 0 || busy !== null} busy={busy === 'sms'}>
            {t('smsResend')}
          </Button>
          {resent ? <p className="text-sm font-semibold text-primary">{t('smsResent')}</p> : null}
        </div>
      ) : null}

      {others.length > 0 ? (
        <section aria-labelledby="mfa-other" className="mt-4 flex flex-col gap-3">
          <h2 id="mfa-other" className="text-base font-semibold">
            {t('other')}
          </h2>
          <ul className="flex flex-col divide-y divide-line overflow-hidden rounded-card border border-line bg-surface">
            {others.map((m) => (
              <li key={m}>
                <button
                  type="button"
                  disabled={busy !== null}
                  onClick={() => (m === 'sms' ? void sendSms() : switchTo(m))}
                  aria-busy={(m === 'sms' && busy === 'sms') || undefined}
                  className="flex min-h-[56px] w-full items-center justify-between gap-3 px-4 text-start text-base font-semibold hover:bg-tint-teal/40 disabled:opacity-60"
                >
                  <span>{m === 'sms' ? t('useSms') : m === 'totp' ? t('useTotp') : t('useRecovery')}</span>
                  {m === 'sms' && phone ? (
                    <bdi dir="ltr" className="text-sm font-semibold text-muted">
                      {phone}
                    </bdi>
                  ) : null}
                </button>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
