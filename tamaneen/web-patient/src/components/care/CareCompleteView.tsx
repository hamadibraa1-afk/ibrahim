'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { CareClosure, CareReturnResponse } from '@/lib/api/types';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { Button, buttonClass } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { PageHeading } from '@/components/ui/PageHeading';

/**
 * P53 End of care, after a supervisor signed off the closing summary: the thanks, the relapse-prevention plan
 * (hidden when there is none), what stays (library, journal, safety plan, Help now; Help now stays in the header),
 * the download of the summary (no diagnosis, no scores) and the one-tap way back to sessions.
 */
export function CareCompleteView({ closure }: { closure: CareClosure }) {
  const t = useTranslations('care.complete');
  const te = useTranslations('errors');
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function comeBack() {
    setBusy(true);
    setError(null);
    try {
      const { next } = await api<CareReturnResponse>('me/care/return', { method: 'POST' });
      router.push(next === '/therapists' ? '/therapists' : '/matches');
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? te('network') : te('generic')));
      setBusy(false);
    }
  }

  const hasPlan = Boolean(closure.warningSigns || closure.whatToDo);
  return (
    <div className="flex flex-col gap-4">
      <PageHeading eyebrow={t('eyebrow')} title={t('title')} subtitle={t('sessions', { count: closure.sessionCount, name: closure.therapistName })} />
      {closure.summary ? (
        <Card>
          <p className="whitespace-pre-line" data-testid="closure-summary">
            {closure.summary}
          </p>
        </Card>
      ) : null}
      {hasPlan ? (
        <Card tone="sand" data-testid="relapse-plan">
          <h2 className="mb-3 text-lg font-bold">{t('planTitle')}</h2>
          <ul className="flex flex-col gap-2">
            {closure.warningSigns ? <li>{t('signs', { text: closure.warningSigns })}</li> : null}
            {closure.whatToDo ? <li>{t('todo', { text: closure.whatToDo })}</li> : null}
            {closure.followUp === 'phq9_1m_3m' ? <li>{t('reminder')}</li> : null}
          </ul>
        </Card>
      ) : null}
      <Card tone="teal" data-testid="keeps">
        <h2 className="mb-2 text-lg font-bold">{t('keepsTitle')}</h2>
        <p>{closure.keepLibrary ? t('keeps') : t('keepsNoLibrary')}</p>
      </Card>
      <a href="/api/v1/me/care-closure/summary" download="summary.txt" className={buttonClass('primary', true)} data-testid="download-summary">
        {t('download')}
      </a>
      {error ? <Notice tone="danger">{error}</Notice> : null}
      <Button variant="secondary" full busy={busy} onClick={comeBack} data-testid="come-back">
        {busy ? t('returning') : t('return')}
      </Button>
      <p className="text-center text-sm text-muted">{closure.returnRequested ? t('returnRequested') : t('returnNote')}</p>
    </div>
  );
}
