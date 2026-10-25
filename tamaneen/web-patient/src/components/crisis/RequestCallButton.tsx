'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import type { HelpResponse } from '@/lib/api/types';
import { apiMessage } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';
import { HELP_SENT_EVENT, requestHelp } from './helpNow';

/**
 * "Ask a professional to call me now": POST /v1/crisis/help always opens a level 4 event. By default the
 * patient then lands on the crisis screen (P06); the crisis screen itself passes `onRequested` instead.
 * The request goes through the Help now queue: offline it waits and is sent when the connection returns
 * (EC-04), with the same idempotency key, so pressing again never opens a second event.
 */
export function RequestCallButton({ label, hint, onRequested }: { label: string; hint?: string; onRequested?: (response: HelpResponse) => void }) {
  const tErrors = useTranslations('errors');
  const t = useTranslations('crisis');
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [queued, setQueued] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A queued request was delivered later (by this page or the header's retry).
  useEffect(() => {
    if (!queued) return;
    const onSent = (event: Event) => {
      setQueued(false);
      if (onRequested) onRequested((event as CustomEvent<HelpResponse>).detail);
    };
    window.addEventListener(HELP_SENT_EVENT, onSent);
    return () => window.removeEventListener(HELP_SENT_EVENT, onSent);
  }, [queued, onRequested]);

  async function request() {
    setBusy(true);
    setError(null);
    const result = await requestHelp();
    setBusy(false);
    if (result.status === 'sent') {
      setQueued(false);
      if (onRequested) onRequested(result.response);
      else router.replace('/crisis');
    } else if (result.status === 'queued') {
      setQueued(true);
    } else {
      setError(apiMessage(result.error) ?? tErrors('generic'));
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Button full busy={busy} onClick={request} data-testid="request-call">
        {label}
      </Button>
      {hint ? <p className="text-center text-sm text-muted">{hint}</p> : null}
      <div aria-live="polite" className="empty:hidden">
        {queued ? (
          <p data-testid="request-queued" className="text-sm font-semibold text-help">
            {t('helpNow.queued')}
          </p>
        ) : null}
      </div>
      {error ? (
        <p role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </div>
  );
}
