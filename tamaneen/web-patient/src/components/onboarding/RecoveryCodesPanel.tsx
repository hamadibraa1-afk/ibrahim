'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { isLocale } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import type { RecoveryCodes } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { Spinner } from '@/components/ui/Spinner';
import { problemText } from '@/components/auth/errorText';
import { CheckboxRow } from './CheckboxRow';
import { FLOW_KEYS, clearFlow, isRecoveryCodes, readFlow } from './flowStorage';
import { StepHeading } from './StepHeading';
import { TotpSetup } from './TotpSetup';

export const RECOVERY_FILE_NAME = 'tamaneen-recovery-codes.txt';

type Status = 'copied' | 'copyFailed' | 'downloaded' | null;

/**
 * P17: the ten recovery codes, shown once. They arrive from P16 through sessionStorage and are removed
 * when the patient continues. If they are gone (new tab, reload after clearing), the patient can create
 * a new set, which replaces the old one.
 */
export function RecoveryCodesPanel() {
  const t = useTranslations('recoveryCodes');
  const tu = useTranslations('ui');
  const te = useTranslations('errors');
  const locale = useLocale();
  const router = useRouter();
  const hintId = useId();
  const [codes, setCodes] = useState<string[] | null | 'missing'>(null);
  const [saved, setSaved] = useState(false);
  const [status, setStatus] = useState<Status>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setCodes(readFlow(FLOW_KEYS.recoveryCodes, isRecoveryCodes) ?? 'missing');
  }, []);

  async function regenerate() {
    setBusy(true);
    setError(null);
    try {
      const response = await api<RecoveryCodes>('me/recovery-codes', { method: 'POST' });
      setCodes(response.recoveryCodes);
    } catch (e) {
      setError(problemText(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  if (codes === null) return <Spinner label={tu('loading')} />;

  if (codes === 'missing') {
    return (
      <div className="flex flex-col gap-5">
        <StepHeading title={t('missingTitle')} subtitle={t('missingBody')} />
        {error ? <Notice tone="danger">{error}</Notice> : null}
        <Button full onClick={regenerate} busy={busy}>
          {t('regenerate')}
        </Button>
      </div>
    );
  }

  const list = codes;

  function download() {
    const created = formatDate(new Date().toISOString(), isLocale(locale) ? locale : 'ar', undefined, { day: 'numeric', month: 'long', year: 'numeric' });
    const text = [t('fileTitle'), t('fileBody'), t('fileDate', { date: created }), '', ...list, ''].join('\r\n');
    const url = URL.createObjectURL(new Blob(['﻿', text], { type: 'text/plain;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = RECOVERY_FILE_NAME;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    setStatus('downloaded');
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(list.join('\n'));
      setStatus('copied');
    } catch {
      setStatus('copyFailed');
    }
  }

  function finish() {
    clearFlow(FLOW_KEYS.recoveryCodes, FLOW_KEYS.signup, FLOW_KEYS.eligibility);
    router.replace('/start/consents');
  }

  return (
    <div className="flex flex-col gap-5">
      <StepHeading title={t('title')} subtitle={t('subtitle')} focusKey={list[0]} />

      <Card className="border border-line">
        <ol aria-label={t('listLabel')} dir="ltr" data-testid="recovery-codes" className="grid grid-cols-2 gap-2">
          {list.map((code) => (
            <li key={code} className="rounded-[10px] bg-tint-sand px-2 py-2.5 text-center font-mono text-base font-bold tracking-wider sm:text-lg">
              {code}
            </li>
          ))}
        </ol>
      </Card>

      <div className="grid grid-cols-2 gap-3">
        <Button variant="secondary" onClick={download}>
          {t('download')}
        </Button>
        <Button variant="secondary" onClick={copy}>
          {t('copy')}
        </Button>
      </div>
      <div aria-live="polite" className="-mt-2 min-h-5 text-center text-sm font-semibold">
        {status === 'copied' ? <span className="text-primary">{t('copied')}</span> : null}
        {status === 'downloaded' ? <span className="text-primary">{t('downloaded')}</span> : null}
        {status === 'copyFailed' ? <span className="text-help">{t('copyFailed')}</span> : null}
      </div>

      <TotpSetup />

      <div className="mt-4 flex flex-col gap-4">
        <CheckboxRow checked={saved} onChange={setSaved} label={t('savedCheck')} describedBy={saved ? undefined : hintId} />
        {saved ? null : (
          <p id={hintId} className="-mt-2 text-sm text-muted">
            {t('mustConfirm')}
          </p>
        )}
        <Button full disabled={!saved} onClick={finish}>
          {tu('continue')}
        </Button>
      </div>
    </div>
  );
}
