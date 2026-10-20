'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { HelpResponse } from '@/lib/api/types';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';

/**
 * "Ask a professional to call me now": POST /v1/crisis/help always opens a level 4 event. By default the
 * patient then lands on the crisis screen (P06); the crisis screen itself passes `onRequested` instead.
 */
export function RequestCallButton({ label, hint, onRequested }: { label: string; hint?: string; onRequested?: (response: HelpResponse) => void }) {
  const tErrors = useTranslations('errors');
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function request() {
    setBusy(true);
    setError(null);
    try {
      const response = await api<HelpResponse>('crisis/help', { method: 'POST' });
      if (onRequested) onRequested(response);
      else router.replace('/crisis');
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? tErrors('network') : tErrors('generic')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-2">
      <Button full busy={busy} onClick={request} data-testid="request-call">
        {label}
      </Button>
      {hint ? <p className="text-center text-sm text-muted">{hint}</p> : null}
      {error ? (
        <p role="alert" className="text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </div>
  );
}
