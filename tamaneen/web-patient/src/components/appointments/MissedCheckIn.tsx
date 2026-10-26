'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Link } from '@/i18n/navigation';
import { Card } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { TalkNowButton } from './TalkNowButton';

export const FEELINGS = ['ok', 'tired', 'not_ok'] as const;
export type Feeling = (typeof FEELINGS)[number];

/**
 * P63 «كيف حالك الآن؟». «مو بخير» shows the crisis resources and the option to talk to someone now. The answer is
 * not sent: the API has no check-in endpoint for a missed session yet (the spec marks it as proposed).
 */
export function MissedCheckIn() {
  const t = useTranslations('appointmentDetail.missed');
  const [feeling, setFeeling] = useState<Feeling | null>(null);
  return (
    <Card className="flex flex-col gap-3">
      <h2 className="text-sm font-semibold text-muted">{t('howNow')}</h2>
      <div role="group" aria-label={t('howNow')} className="flex flex-wrap gap-2">
        {FEELINGS.map((f) => (
          <Chip key={f} selected={feeling === f} onClick={() => setFeeling(f)} data-testid={`feeling-${f}`}>
            {t(`feelings.${f}`)}
          </Chip>
        ))}
      </div>
      {feeling === 'not_ok' ? (
        <div role="status" data-testid="not-ok-support" className="flex flex-col gap-3 rounded-[16px] border-[1.5px] border-help p-4">
          <p className="text-sm leading-relaxed">{t('notOkBody')}</p>
          <TalkNowButton label={t('talkNow')} variant="danger-outline" />
          <Link href="/crisis-resources" className="text-sm font-semibold text-primary underline-offset-4 hover:underline">
            {t('resources')}
          </Link>
        </div>
      ) : feeling ? (
        <p role="status" className="text-sm text-muted">
          {t(feeling === 'ok' ? 'okReply' : 'tiredReply')}
        </p>
      ) : null}
    </Card>
  );
}
