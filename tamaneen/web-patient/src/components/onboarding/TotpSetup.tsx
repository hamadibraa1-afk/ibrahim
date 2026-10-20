'use client';

import { useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { api } from '@/lib/api/client';
import type { TotpSetup as TotpSetupResponse } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { CODE_LENGTH, normalizeCode } from '@/components/auth/contact';
import { fieldErrorsOf, problemText } from '@/components/auth/errorText';

/** "ABCDEFGHIJKL" → "ABCD EFGH IJKL", easier to type into an authenticator app. */
export function groupSecret(secret: string): string {
  return (secret.replace(/\s/g, '').match(/.{1,4}/g) ?? []).join(' ');
}

/** Optional authenticator app (TOTP) on P17. No QR library: the key is shown as text plus an otpauth link. */
export function TotpSetup() {
  const t = useTranslations('recoveryCodes');
  const te = useTranslations('errors');
  const [setup, setSetup] = useState<TotpSetupResponse | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function start() {
    setBusy(true);
    setError(null);
    try {
      setSetup(await api<TotpSetupResponse>('me/totp/setup', { method: 'POST' }));
    } catch (e) {
      setError(problemText(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  async function confirm(e: FormEvent) {
    e.preventDefault();
    if (code.length !== CODE_LENGTH) {
      setError(t('totpCodeInvalid'));
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await api<void>('me/totp/confirm', { method: 'POST', body: { code } });
      setDone(true);
    } catch (err) {
      setError(fieldErrorsOf(err).code ?? problemText(err, te('network')));
    } finally {
      setBusy(false);
    }
  }

  if (done) {
    return <Notice tone="success">{t('totpEnabled')}</Notice>;
  }

  const safeUri = setup && setup.otpauthUri.startsWith('otpauth://') ? setup.otpauthUri : null;

  return (
    <Card className="flex flex-col gap-4 border border-line">
      <div className="flex items-center justify-between gap-3">
        <div className="flex flex-col">
          <h2 className="text-base font-bold">{t('extraTitle')}</h2>
          <p className="text-sm text-muted">{t('extraBody')}</p>
        </div>
        {setup ? null : (
          <Button variant="secondary" compact onClick={start} busy={busy}>
            {t('setup')}
          </Button>
        )}
      </div>

      {setup ? (
        <form onSubmit={confirm} noValidate className="flex flex-col gap-3">
          <p className="text-sm leading-relaxed">{t('totpIntro')}</p>
          <div className="flex flex-col gap-1">
            <p className="text-sm font-semibold">{t('secretLabel')}</p>
            <p dir="ltr" data-testid="totp-secret" className="select-all rounded-[12px] bg-tint-sand px-4 py-3 text-center font-mono text-lg font-bold tracking-wider">
              {groupSecret(setup.secret)}
            </p>
          </div>
          {safeUri ? (
            <a href={safeUri} className="inline-flex min-h-touch items-center text-sm font-semibold text-primary underline underline-offset-4">
              {t('openApp')}
            </a>
          ) : null}
          <TextField
            label={t('totpCode')}
            value={code}
            onChange={(e) => {
              setCode(normalizeCode(e.target.value));
              setError(null);
            }}
            dir="ltr"
            inputMode="numeric"
            autoComplete="one-time-code"
            maxLength={CODE_LENGTH}
            error={error}
            className="text-center font-mono text-xl tracking-[0.4em]"
          />
          <Button type="submit" variant="secondary" busy={busy}>
            {t('confirm')}
          </Button>
        </form>
      ) : error ? (
        <p role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </Card>
  );
}
