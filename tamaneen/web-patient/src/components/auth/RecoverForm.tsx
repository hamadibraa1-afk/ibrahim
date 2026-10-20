'use client';

import { useTranslations } from 'next-intl';
import { useId, useState, type FormEvent } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { ChallengeResponse, RecoverCompleteRequest, RecoverStartRequest, RecoverWithCodeRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { StepHeading } from '@/components/onboarding/StepHeading';
import { CODE_LENGTH, MIN_PASSWORD_LENGTH, isValidContact, normalizeCode, normalizeContact, normalizeRecoveryCode } from './contact';
import { fieldErrorsOf, problemText } from './errorText';
import { PasswordField } from './PasswordField';
import { ResendCountdown } from './ResendCountdown';

type Method = 'code' | 'recovery';
type Errors = Partial<Record<'contact' | 'code' | 'recoveryCode' | 'newPassword' | 'form', string>>;

/**
 * P20 Recover. By a code to the phone or e-mail (the API answers the same whether or not the account
 * exists, and so does this screen), or with one of the ten recovery codes. Success → login with a notice.
 */
export function RecoverForm() {
  const t = useTranslations('recover');
  const te = useTranslations('errors');
  const router = useRouter();
  const groupId = useId();
  const [method, setMethod] = useState<Method>('code');
  const [contact, setContact] = useState('');
  const [challenge, setChallenge] = useState<{ challengeId: string; availableAt: number } | null>(null);
  const [code, setCode] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState(false);
  const [returned, setReturned] = useState(false);

  function fail(err: unknown) {
    const f = fieldErrorsOf(err);
    const mapped: Errors = { contact: f.contact, code: f.code, recoveryCode: f.recoveryCode, newPassword: f.newPassword };
    const hasField = Object.values(mapped).some(Boolean);
    setErrors({ ...mapped, form: hasField ? undefined : problemText(err, te('network')) });
  }

  function checkPassword(next: Errors) {
    if (newPassword.length < MIN_PASSWORD_LENGTH) next.newPassword = t('passwordTooShort');
  }

  async function requestCode(e: FormEvent) {
    e.preventDefault();
    if (!isValidContact(contact)) {
      setErrors({ contact: t('contactInvalid') });
      return;
    }
    setBusy(true);
    setErrors({});
    try {
      const body: RecoverStartRequest = { contact: normalizeContact(contact) };
      const response = await api<ChallengeResponse>('auth/recover', { method: 'POST', body });
      setChallenge({ challengeId: response.challengeId, availableAt: Date.now() + response.resendAfterSeconds * 1000 });
    } catch (err) {
      fail(err);
    } finally {
      setBusy(false);
    }
  }

  async function complete(e: FormEvent) {
    e.preventDefault();
    if (!challenge) return;
    const next: Errors = {};
    if (code.length !== CODE_LENGTH) next.code = t('codeInvalid');
    checkPassword(next);
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    setBusy(true);
    try {
      const body: RecoverCompleteRequest = { challengeId: challenge.challengeId, code, newPassword };
      await api<void>('auth/recover/complete', { method: 'POST', body });
      router.push('/login?reset=1');
    } catch (err) {
      fail(err);
      setBusy(false);
    }
  }

  async function withRecoveryCode(e: FormEvent) {
    e.preventDefault();
    const next: Errors = {};
    if (!isValidContact(contact)) next.contact = t('contactInvalid');
    if (normalizeRecoveryCode(recoveryCode).replace(/-/g, '').length < 8) next.recoveryCode = t('recoveryInvalid');
    checkPassword(next);
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    setBusy(true);
    try {
      const body: RecoverWithCodeRequest = { contact: normalizeContact(contact), recoveryCode: normalizeRecoveryCode(recoveryCode), newPassword };
      await api<void>('auth/recover/code', { method: 'POST', body });
      router.push('/login?reset=1');
    } catch (err) {
      fail(err);
      setBusy(false);
    }
  }

  const contactField = (
    <TextField
      label={t('contact')}
      value={contact}
      onChange={(e) => setContact(e.target.value)}
      dir="ltr"
      type="text"
      inputMode="email"
      autoComplete="username"
      autoCapitalize="none"
      spellCheck={false}
      placeholder="05X XXX XXXX"
      error={errors.contact}
      className="text-start"
    />
  );

  const passwordField = <PasswordField label={t('newPassword')} value={newPassword} onChange={setNewPassword} autoComplete="new-password" error={errors.newPassword} showStrength />;
  const formError = errors.form ? <Notice tone="danger">{errors.form}</Notice> : null;
  const lostBoth = (
    <Card className="border border-line text-sm leading-relaxed">
      <p>{t('lostBoth')}</p>
    </Card>
  );

  if (method === 'code' && challenge) {
    return (
      <form key="reset" onSubmit={complete} noValidate className="flex flex-col gap-5">
        <StepHeading title={t('title')} focusOnMount />
        <Notice tone="info">{t('neutral')}</Notice>
        <TextField
          label={t('code')}
          value={code}
          onChange={(e) => setCode(normalizeCode(e.target.value))}
          dir="ltr"
          inputMode="numeric"
          autoComplete="one-time-code"
          pattern="[0-9]*"
          maxLength={CODE_LENGTH}
          error={errors.code}
          className="text-center font-mono text-2xl font-bold tracking-[0.5em]"
        />
        <ResendCountdown challengeId={challenge.challengeId} availableAt={challenge.availableAt} onChange={(next) => setChallenge({ challengeId: next.challengeId, availableAt: next.availableAt })} />
        {passwordField}
        {formError}
        <Button type="submit" full busy={busy}>
          {t('reset')}
        </Button>
        <Button
          variant="ghost"
          full
          onClick={() => {
            setChallenge(null);
            setReturned(true);
            setCode('');
            setErrors({});
          }}
        >
          {t('changeContact')}
        </Button>
      </form>
    );
  }

  return (
    <form key="start" onSubmit={method === 'code' ? requestCode : withRecoveryCode} noValidate className="flex flex-col gap-5">
      <StepHeading title={t('title')} focusOnMount={returned} />

      <fieldset className="overflow-hidden rounded-card border border-line bg-surface">
        <legend id={groupId} className="sr-only">
          {t('methodLegend')}
        </legend>
        {(['code', 'recovery'] as const).map((m, i) => (
          <label key={m} className={`flex cursor-pointer items-start gap-3 p-4 ${i > 0 ? 'border-t border-line' : ''}`}>
            <input
              type="radio"
              name={`${groupId}-method`}
              value={m}
              checked={method === m}
              onChange={() => {
                setMethod(m);
                setErrors({});
              }}
              className="mt-1 h-6 w-6 shrink-0 accent-[rgb(var(--primary))]"
            />
            <span className="flex flex-col gap-0.5">
              <span className="text-base font-bold">{m === 'code' ? t('byCode') : t('byRecovery')}</span>
              <span className="text-sm text-muted">{m === 'code' ? t('byCodeHint') : t('byRecoveryHint')}</span>
            </span>
          </label>
        ))}
      </fieldset>

      {contactField}

      {method === 'code' ? (
        <p className="-mt-2 text-sm leading-relaxed text-muted">{t('neutral')}</p>
      ) : (
        <>
          <TextField
            label={t('recoveryCode')}
            value={recoveryCode}
            onChange={(e) => setRecoveryCode(normalizeRecoveryCode(e.target.value))}
            dir="ltr"
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={20}
            placeholder="XXXX-XXXX"
            error={errors.recoveryCode}
            className="text-center font-mono text-xl tracking-widest"
          />
          {passwordField}
        </>
      )}

      {lostBoth}
      {formError}

      <Button type="submit" full busy={busy} className="mt-4">
        {method === 'code' ? t('send') : t('reset')}
      </Button>
    </form>
  );
}
