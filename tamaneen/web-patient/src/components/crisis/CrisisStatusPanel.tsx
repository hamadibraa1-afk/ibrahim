'use client';

import { useTranslations } from 'next-intl';
import { useCallback, useEffect, useState } from 'react';
import { api } from '@/lib/api/client';
import type { CrisisStatus } from '@/lib/api/types';
import { RequestCallButton } from './RequestCallButton';

export const CRISIS_POLL_MS = 20_000;

function StepMark({ done }: { done: boolean }) {
  return done ? (
    <span aria-hidden="true" className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-primary text-surface">
      <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round">
        <path d="M5 12l4 4 10-10" />
      </svg>
    </span>
  ) : (
    <span aria-hidden="true" className="h-11 w-11 shrink-0 rounded-full border-[3px] border-primary" />
  );
}

/**
 * The "team notified → a professional is calling you" steps of P06. Polls /v1/crisis/status every 20 s
 * until a professional acknowledges the event, and announces that change politely.
 */
export function CrisisStatusPanel({ initial }: { initial: CrisisStatus | null }) {
  const t = useTranslations('crisis');
  const [status, setStatus] = useState<CrisisStatus | null>(initial);
  const [failed, setFailed] = useState(false);

  const refresh = useCallback(async () => {
    try {
      setStatus(await api<CrisisStatus>('crisis/status'));
      setFailed(false);
    } catch {
      setFailed(true);
    }
  }, []);

  const active = status !== null && (status.eventOpen || status.locked);
  const waiting = status === null || (active && !status.acknowledged);

  useEffect(() => {
    if (initial === null) void refresh();
  }, [initial, refresh]);

  useEffect(() => {
    if (!waiting) return;
    const id = window.setInterval(() => {
      if (document.visibilityState !== 'hidden') void refresh();
    }, CRISIS_POLL_MS);
    return () => window.clearInterval(id);
  }, [waiting, refresh]);

  if (status === null) {
    // Status unknown (the API did not answer). The numbers below the panel still work; offer the call.
    return failed ? <RequestCallButton label={t('requestCall')} onRequested={() => void refresh()} /> : null;
  }

  if (!active) {
    return (
      <section aria-labelledby="crisis-no-event" className="flex flex-col gap-3 rounded-card border-[1.5px] border-line bg-surface p-5">
        <h2 id="crisis-no-event" className="text-lg font-bold">
          {t('noEventTitle')}
        </h2>
        <p className="leading-relaxed">{t('noEventBody')}</p>
        <RequestCallButton label={t('requestCall')} onRequested={() => void refresh()} />
      </section>
    );
  }

  return (
    <div className="flex flex-col gap-4">
      <ol aria-label={t('stepsLabel')} className="flex flex-col gap-3 rounded-card border-[1.5px] border-line bg-surface p-5" data-testid="crisis-steps">
        <li className="flex items-center gap-4">
          <StepMark done />
          <p className="text-lg font-bold">
            <span className="sr-only">{t('stepDone')} </span>
            {t('notified')}
          </p>
        </li>
        <li aria-hidden="true" className="ms-5 h-6 w-0.5 bg-line" />
        <li className="flex items-center gap-4">
          <StepMark done={status.acknowledged} />
          <div className="flex flex-col gap-0.5">
            <p className="text-lg font-bold">
              <span className="sr-only">{status.acknowledged ? t('stepDone') : t('stepPending')} </span>
              {t('calling')}
            </p>
            {status.contactPhoneLast4 ? (
              <p className="text-sm text-muted">
                {t.rich('lastDigits', {
                  last4: status.contactPhoneLast4,
                  num: (chunks) => (
                    <bdi dir="ltr" className="tabular-nums">
                      {chunks}
                    </bdi>
                  ),
                })}
              </p>
            ) : null}
          </div>
        </li>
      </ol>
      <div aria-live="polite" className="empty:hidden">
        {status.acknowledged ? (
          <p data-testid="crisis-acknowledged" className="rounded-[16px] bg-tint-teal px-4 py-3 font-semibold text-primary-strong">
            {t('acknowledged')}
          </p>
        ) : null}
      </div>
      {status.script ? (
        <section aria-labelledby="crisis-script" className="flex flex-col gap-2 rounded-card border-[1.5px] border-primary bg-surface p-5">
          <h2 id="crisis-script" className="font-bold">
            {t('scriptTitle')}
          </h2>
          <p className="whitespace-pre-line leading-relaxed">{status.script}</p>
        </section>
      ) : null}
    </div>
  );
}
