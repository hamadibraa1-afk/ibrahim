'use client';

import { useTranslations } from 'next-intl';
import type { CareClosure } from '@/lib/api/types';
import { ButtonLink, buttonClass } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { PageHeading } from '@/components/ui/PageHeading';

/**
 * P53 End of care, after a supervisor signed off the closing summary: the thanks, the relapse-prevention plan
 * (hidden when there is none), what stays (library, journal, safety plan, Help now; Help now stays in the header),
 * the download of the summary (no diagnosis, no scores) and the way back to sessions, through the short safety
 * check (/care/return) the design asks for.
 */
export function CareCompleteView({ closure }: { closure: CareClosure }) {
  const t = useTranslations('care.complete');

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
      <ButtonLink href="/care/return" variant="secondary" full data-testid="come-back">
        {t('return')}
      </ButtonLink>
      <p className="text-center text-sm text-muted">{closure.returnRequested ? t('returnRequested') : t('returnNote')}</p>
    </div>
  );
}
