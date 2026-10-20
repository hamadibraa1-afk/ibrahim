'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState, type FormEvent } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { authApi } from '@/lib/api/client';
import { Button } from '@/components/ui/Button';
import { Spinner } from '@/components/ui/Spinner';
import { TextField } from '@/components/ui/TextField';
import { CODE_LENGTH, normalizeCode } from '@/components/auth/contact';
import { fieldErrorsOf, problemText } from '@/components/auth/errorText';
import { ResendCountdown } from '@/components/auth/ResendCountdown';
import { FLOW_KEYS, clearFlow, isSignupState, readFlow, writeFlow, type SignupState } from './flowStorage';
import { StepHeading } from './StepHeading';

/** P16 Verify, step 2 of 4: the six-digit code sent at sign-up. Success sets the session (BFF cookies). */
export function VerifyForm() {
  const t = useTranslations('verify');
  const te = useTranslations('errors');
  const tu = useTranslations('ui');
  const router = useRouter();
  const [state, setState] = useState<SignupState | null>(null);
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const saved = readFlow(FLOW_KEYS.signup, isSignupState);
    if (!saved) {
      router.replace('/start/signup?restart=1');
      return;
    }
    setState(saved);
  }, [router]);

  if (!state) return <Spinner label={tu('loading')} />;

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!state) return;
    if (code.length !== CODE_LENGTH) {
      setError(t('codeInvalid'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { recoveryCodes } = await authApi<{ recoveryCodes: string[] }>('signup-verify', { challengeId: state.challengeId, code });
      writeFlow(FLOW_KEYS.recoveryCodes, recoveryCodes);
      clearFlow(FLOW_KEYS.signup, FLOW_KEYS.eligibility);
      router.replace('/start/recovery-codes');
    } catch (err) {
      setError(fieldErrorsOf(err).code ?? problemText(err, te('network')));
      setBusy(false);
    }
  }

  const sentText = state.contactType === 'phone' ? 'sentPhone' : 'sentEmail';
  const availableAt = state.sentAt + state.resendAfterSeconds * 1000;

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <StepHeading title={t('title')} subtitle={t.rich(sentText, { contact: state.maskedContact, b: (chunks) => <bdi dir="ltr">{chunks}</bdi> })} />

      <TextField
        label={t('codeLabel')}
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
        name="code"
        data-testid="code-input"
        className="text-center font-mono text-2xl font-bold tracking-[0.5em]"
      />

      <ResendCountdown
        challengeId={state.challengeId}
        availableAt={availableAt}
        onChange={(next) => {
          const updated: SignupState = {
            ...state,
            challengeId: next.challengeId,
            maskedContact: next.maskedContact ?? state.maskedContact,
            sentAt: Date.now(),
            resendAfterSeconds: Math.max(0, Math.round((next.availableAt - Date.now()) / 1000)),
          };
          writeFlow(FLOW_KEYS.signup, updated);
          setState(updated);
        }}
      />

      <Link href="/start/signup" className="mx-auto inline-flex min-h-touch items-center text-sm font-semibold text-primary underline underline-offset-4">
        {state.contactType === 'phone' ? t('changePhone') : t('changeEmail')}
      </Link>

      <Button type="submit" full busy={busy} className="mt-6">
        {t('submit')}
      </Button>
    </form>
  );
}
