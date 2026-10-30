'use client';

import { useTranslations } from 'next-intl';
import { useRef, useState, type ReactNode } from 'react';
import { Link } from '@/i18n/navigation';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { api } from '@/lib/api/client';
import type { MissedCheckInSaved, MissedFeeling } from '@/lib/api/types';
import { TalkNowButton } from './TalkNowButton';

export const FEELINGS = ['ok', 'tired', 'not_ok'] as const satisfies readonly MissedFeeling[];
export type Feeling = MissedFeeling;

/**
 * P63 «كيف حالك الآن؟». Each answer is sent to POST /v1/appointments/{id}/missed-check-in (stored and audited); «مو
 * بخير» also alerts the care team on the server. «مو بخير» shows the crisis resources and talk-now at once, whether
 * or not the answer reached the server, and a failed send offers a retry.
 */
export function MissedCheckIn({ appointmentId, initial = null, resources }: { appointmentId: string; initial?: Feeling | null; resources?: ReactNode }) {
  const t = useTranslations('appointmentDetail.missed');
  const [feeling, setFeeling] = useState<Feeling | null>(initial);
  const [failed, setFailed] = useState<Feeling | null>(null);
  const keys = useRef(new Map<Feeling, string>());

  async function send(next: Feeling) {
    setFeeling(next);
    setFailed(null);
    // One key per answer: a retry of the same answer is a resend, a different answer is a new write.
    let key = keys.current.get(next);
    if (!key) {
      key = crypto.randomUUID();
      keys.current.set(next, key);
    }
    try {
      await api<MissedCheckInSaved>(`appointments/${encodeURIComponent(appointmentId)}/missed-check-in`, { method: 'POST', body: { feeling: next }, idempotencyKey: key });
      keys.current.delete(next);
    } catch {
      setFailed(next);
    }
  }

  return (
    <Card className="flex flex-col gap-3">
      <h2 className="text-sm font-semibold text-muted">{t('howNow')}</h2>
      <div role="group" aria-label={t('howNow')} className="flex flex-wrap gap-2">
        {FEELINGS.map((f) => (
          <Chip key={f} selected={feeling === f} onClick={() => void send(f)} data-testid={`feeling-${f}`}>
            {t(`feelings.${f}`)}
          </Chip>
        ))}
      </div>
      {failed ? (
        <div role="alert" className="flex flex-col gap-2 text-sm">
          <p>{t('sendError')}</p>
          <Button variant="secondary" compact onClick={() => void send(failed)}>
            {t('retry')}
          </Button>
        </div>
      ) : null}
      {feeling === 'not_ok' ? (
        <div role="status" data-testid="not-ok-support" className="flex flex-col gap-3 rounded-[16px] border-[1.5px] border-help p-4">
          <p className="text-sm leading-relaxed">{t('notOkBody')}</p>
          {resources}
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
