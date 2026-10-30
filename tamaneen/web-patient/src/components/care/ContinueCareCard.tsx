'use client';

import { useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { api } from '@/lib/api/client';
import type { ContinueCare } from '@/lib/api/types';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';

/** Ongoing care: records the patient's wish to continue, which blocks S57 closure (§10.4). */
export function ContinueCareCard({ initial }: { initial: ContinueCare }) {
  const t = useTranslations('care.continue');
  const te = useTranslations('errors');
  const [request, setRequest] = useState(initial);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sending = useRef(false);

  async function submit() {
    if (sending.current) return;
    sending.current = true;
    setBusy(true);
    setError(null);
    try {
      setRequest(await api<ContinueCare>('me/care/continue', { method: 'POST' }));
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? te('network') : te('generic')));
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  return (
    <Card className="mt-3 flex flex-col gap-3" data-testid="continue-care">
      <h2 className="font-bold">{t('title')}</h2>
      {request.requested ? <Notice tone="success">{t('sent')}</Notice> : (
        <>
          <p className="text-sm text-muted">{t('body')}</p>
          {error ? <Notice tone="danger">{error}</Notice> : null}
          <Button variant="secondary" busy={busy} onClick={submit} data-testid="continue-care-submit">
            {error ? t('retry') : t('submit')}
          </Button>
        </>
      )}
    </Card>
  );
}
