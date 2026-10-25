'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useId, useRef, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { RecoveryCodes } from '@/lib/api/types';
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

/** The downloaded file holds the codes only, one per line: no app name, date or account detail (PB-09). */
export function recoveryFileText(codes: readonly string[]): string {
  return `${codes.join('\r\n')}\r\n`;
}

/**
 * P17: the ten recovery codes, shown once. They arrive from P16 through sessionStorage and are removed
 * from it as soon as they are read, so they live only in memory: a reload does not show them again and
 * offers a new set instead, which replaces the old one. Leaving before continuing asks for confirmation,
 * and Continue needs a download or a copy plus the "I saved them" box (PB-09).
 */
export function RecoveryCodesPanel() {
  const t = useTranslations('recoveryCodes');
  const tu = useTranslations('ui');
  const te = useTranslations('errors');
  const router = useRouter();
  const hintId = useId();
  const [codes, setCodes] = useState<string[] | null | 'missing'>(null);
  const [saved, setSaved] = useState(false);
  const [status, setStatus] = useState<Status>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [kept, setKept] = useState(false);
  const leaving = useRef(false);

  // Read once (React may run effects twice in development) and clear the stored copy straight away.
  const loaded = useRef(false);
  useEffect(() => {
    if (loaded.current) return;
    loaded.current = true;
    setCodes(readFlow(FLOW_KEYS.recoveryCodes, isRecoveryCodes) ?? 'missing');
    clearFlow(FLOW_KEYS.recoveryCodes);
  }, []);

  // While codes are on screen and not confirmed, closing or reloading the tab asks first.
  const showing = Array.isArray(codes);
  useEffect(() => {
    if (!showing) return;
    const onBeforeUnload = (e: BeforeUnloadEvent) => {
      if (leaving.current) return;
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [showing]);

  async function regenerate() {
    setBusy(true);
    setError(null);
    try {
      const response = await api<RecoveryCodes>('me/recovery-codes', { method: 'POST' });
      setCodes(response.recoveryCodes);
      setKept(false);
      setSaved(false);
      setStatus(null);
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
    const url = URL.createObjectURL(new Blob([recoveryFileText(list)], { type: 'text/plain;charset=utf-8' }));
    const a = document.createElement('a');
    a.href = url;
    a.download = RECOVERY_FILE_NAME;
    document.body.appendChild(a);
    a.click();
    a.remove();
    window.setTimeout(() => URL.revokeObjectURL(url), 1000);
    setStatus('downloaded');
    setKept(true);
  }

  async function copy() {
    try {
      await navigator.clipboard.writeText(list.join('\n'));
      setStatus('copied');
      setKept(true);
    } catch {
      setStatus('copyFailed');
    }
  }

  const ready = saved && kept;

  function finish() {
    if (!ready) return;
    leaving.current = true;
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
        <CheckboxRow checked={saved} onChange={setSaved} label={t('savedCheck')} describedBy={ready ? undefined : hintId} />
        {ready ? null : (
          <p id={hintId} className="-mt-2 text-sm text-muted">
            {kept ? t('mustConfirm') : t('mustKeep')}
          </p>
        )}
        <Button full disabled={!ready} aria-describedby={ready ? undefined : hintId} onClick={finish}>
          {tu('continue')}
        </Button>
      </div>
    </div>
  );
}
