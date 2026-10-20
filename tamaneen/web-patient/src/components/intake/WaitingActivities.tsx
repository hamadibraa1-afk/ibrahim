'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { TriageState } from '@/lib/api/types';
import { clearDraft, loadDraft, saveDraft } from '@/components/pwa/drafts';
import { apiMessage, isCrisis, isNetworkError } from '@/components/pwa/errors';
import { BreathingPacer } from '@/components/safety/BreathingPacer';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextArea } from '@/components/ui/TextField';

const DRAFT_KEY = 'triageAddendum';
const MAX_ADDENDUM = 4000;

type Panel = 'breathing' | 'addendum' | null;

/** P36 "while you wait": the breathing exercise and "add something you forgot" (gated). */
export function WaitingActivities() {
  const t = useTranslations('waitingTriage');
  const tErrors = useTranslations('errors');
  const router = useRouter();
  const [panel, setPanel] = useState<Panel>(null);
  const [text, setText] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);

  useEffect(() => {
    setText(loadDraft(DRAFT_KEY));
  }, []);

  function toggle(next: Exclude<Panel, null>) {
    setPanel((p) => (p === next ? null : next));
  }

  async function send() {
    const trimmed = text.trim();
    if (!trimmed) return;
    setBusy(true);
    setError(null);
    setSent(false);
    try {
      const response = await api<TriageState>('triage/addendum', { method: 'POST', body: { text: trimmed } });
      if (isCrisis(response)) {
        router.replace('/crisis');
        return;
      }
      setText('');
      clearDraft(DRAFT_KEY);
      setSent(true);
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? tErrors('network') : tErrors('generic')));
    } finally {
      setBusy(false);
    }
  }

  const tile = 'flex min-h-[88px] flex-col items-start justify-center gap-1 rounded-card p-4 text-start';

  return (
    <section aria-labelledby="waiting-while" className="flex flex-col gap-3">
      <h2 id="waiting-while" className="text-base font-bold">
        {t('whileWaiting')}
      </h2>
      <div className="grid grid-cols-2 gap-3">
        <button type="button" aria-expanded={panel === 'breathing'} aria-controls="waiting-breathing" onClick={() => toggle('breathing')} className={`${tile} bg-tint-teal text-primary-strong`}>
          <span className="text-lg font-bold">{t('breathingTitle')}</span>
          <span className="text-sm">{t('breathingSub')}</span>
        </button>
        <button type="button" aria-expanded={panel === 'addendum'} aria-controls="waiting-addendum" onClick={() => toggle('addendum')} className={`${tile} bg-tint-sand text-ink`}>
          <span className="text-lg font-bold">{t('addTitle')}</span>
          <span className="text-sm">{t('addSub')}</span>
        </button>
      </div>

      <div id="waiting-breathing" hidden={panel !== 'breathing'}>
        {panel === 'breathing' ? <BreathingPacer /> : null}
      </div>

      <div id="waiting-addendum" hidden={panel !== 'addendum'} className="flex flex-col gap-3 rounded-card bg-surface p-4">
        <TextArea
          label={t('addendumLabel')}
          hint={t('addendumHint')}
          value={text}
          maxLength={MAX_ADDENDUM}
          dir="auto"
          onChange={(e) => {
            setText(e.target.value);
            saveDraft(DRAFT_KEY, e.target.value);
            setSent(false);
          }}
        />
        {error ? (
          <p role="alert" className="text-sm font-semibold text-help">
            {error}
          </p>
        ) : null}
        {sent ? <Notice tone="success">{t('addendumSent')}</Notice> : null}
        <Button busy={busy} disabled={text.trim() === ''} onClick={() => void send()}>
          {t('addendumSend')}
        </Button>
      </div>
    </section>
  );
}
