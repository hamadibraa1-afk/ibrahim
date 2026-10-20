'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { ApiError, authApi } from '@/lib/api/client';
import type { LoginResponse } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { FLOW_KEYS, clearFlow, writeFlow, type MfaState } from '@/components/onboarding/flowStorage';
import { LOCK_MINUTES, normalizeContact, pluralKey } from './contact';
import { fieldErrorsOf, problemText } from './errorText';
import { signedInPath } from './finishSignIn';
import { PasswordField } from './PasswordField';

/** The BFF strips the tokens: the browser sees { status: 'ok' } or the MFA step. */
type LoginResult = { status: 'ok' } | Extract<LoginResponse, { status: 'mfa' }>;

type Errors = Partial<Record<'contact' | 'password', string>>;

/** P18 Login form. Passkeys are not part of this release, so there is no passkey button. */
export function LoginForm({ notice }: { notice?: 'reset' | 'restart' | null }) {
  const t = useTranslations('login');
  const te = useTranslations('errors');
  const locale = useLocale();
  const router = useRouter();
  const [contact, setContact] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<Errors>({});
  const [problem, setProblem] = useState<{ text: string; extra?: string } | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const next: Errors = {};
    if (!contact.trim()) next.contact = t('contactRequired');
    if (!password) next.password = t('passwordRequired');
    setErrors(next);
    if (next.contact || next.password) return;

    setBusy(true);
    setProblem(null);
    try {
      const result = await authApi<LoginResult>('login', { contact: normalizeContact(contact), password });
      if (result.status === 'mfa') {
        const state: MfaState = { mfaToken: result.mfaToken, methods: result.methods, maskedPhone: result.maskedPhone };
        if (!writeFlow(FLOW_KEYS.mfa, state)) {
          setProblem({ text: t('storageBlocked') });
          setBusy(false);
          return;
        }
        router.push('/login/mfa');
        return;
      }
      clearFlow(FLOW_KEYS.mfa);
      router.replace(await signedInPath());
    } catch (err) {
      setBusy(false);
      if (err instanceof ApiError && err.status === 423) {
        const minutes = Math.max(1, Math.ceil((err.problem.retryAfterSeconds ?? LOCK_MINUTES * 60) / 60));
        setProblem({ text: t(`locked.${pluralKey(locale, minutes)}`, { minutes }) });
        return;
      }
      const fields = fieldErrorsOf(err);
      if (fields.contact || fields.password) {
        setErrors({ contact: fields.contact, password: fields.password });
        return;
      }
      const remaining = err instanceof ApiError && err.status === 401 ? err.problem.remainingAttempts : undefined;
      setProblem({
        text: problemText(err, te('network')),
        extra: typeof remaining === 'number' ? t(`attempts.${pluralKey(locale, remaining)}`, { count: remaining, minutes: LOCK_MINUTES }) : undefined,
      });
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      {notice === 'reset' ? <Notice tone="success">{t('resetDone')}</Notice> : null}
      {notice === 'restart' ? <Notice tone="warning">{t('mfaRestart')}</Notice> : null}

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
        name="username"
        className="text-start"
      />
      <PasswordField label={t('password')} value={password} onChange={setPassword} autoComplete="current-password" error={errors.password} name="password" />
      <Link href="/recover" className="-mt-2 inline-flex min-h-touch items-center self-start text-sm font-semibold text-primary underline underline-offset-4">
        {t('forgot')}
      </Link>

      {problem ? (
        <div role="alert" data-testid="login-problem" className="rounded-[16px] bg-help/10 px-4 py-3 text-sm leading-relaxed text-help">
          {problem.text}
          {problem.extra ? <> {problem.extra}</> : null}
        </div>
      ) : null}

      <Button type="submit" full busy={busy}>
        {t('submit')}
      </Button>

      <p className="mt-10 text-center text-sm">
        {t('noAccount')}{' '}
        <Link href="/start/eligibility" className="font-semibold text-primary underline underline-offset-4">
          {t('start')}
        </Link>
      </p>
    </form>
  );
}
