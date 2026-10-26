'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Dialog } from '@/components/booking/Dialog';
import { genderKey } from '@/components/matching/labels';
import { BreathingPacer } from '@/components/safety/BreathingPacer';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import type { TherapistSummary } from '@/lib/api/types';
import { mmss } from './sessionClock';

/**
 * P54 "therapist late", a state of the lobby: the therapist's notice if they sent one, the reassurance that late
 * time is not deducted, a counter since the start (server time), a breathing exercise in an overlay, and the
 * free reschedule (always there, emphasised once the options open) plus another available therapist.
 */
export function LateNotice({
  appointmentId,
  therapist,
  lateMs,
  lateMinutesNotice,
  optionsOpen,
  optionsAfterMinutes,
}: {
  appointmentId: string;
  therapist: TherapistSummary;
  lateMs: number;
  lateMinutesNotice: number | null;
  optionsOpen: boolean;
  optionsAfterMinutes: number;
}) {
  const t = useTranslations('session.late');
  const [breathing, setBreathing] = useState(false);
  const rescheduleHref = `/therapists/${encodeURIComponent(therapist.id)}/book?reschedule=${encodeURIComponent(appointmentId)}`;
  const g = therapist.gender;

  return (
    <section aria-labelledby="late-title" data-testid="late-state" className="flex flex-col gap-4">
      <h2 id="late-title" className="sr-only">
        {t(genderKey('title', g))}
      </h2>
      <Card tone="sand" className="flex flex-col gap-2">
        <p className="text-lg font-bold">{t(genderKey('heading', g), { name: therapist.displayName })}</p>
        <p className="text-base leading-relaxed">
          {lateMinutesNotice !== null ? `${t(genderKey('message', g), { minutes: lateMinutesNotice })} ` : null}
          {t('notDeducted')}
        </p>
        <p className="mt-2 text-center">
          <span dir="ltr" className="block text-4xl font-bold tabular-nums" data-testid="late-counter" aria-live="off">
            {mmss(lateMs)}
          </span>
          <span className="text-sm text-muted">{t('sinceStart')}</span>
        </p>
      </Card>

      <Card className="flex flex-col gap-3 border border-line">
        <p className="font-semibold">{t('whileWaiting')}</p>
        <Button variant="secondary" full onClick={() => setBreathing(true)}>
          {t('breathing')}
        </Button>
      </Card>

      <p className="text-sm leading-relaxed text-muted">{optionsOpen ? t('optionsNow', { minutes: optionsAfterMinutes }) : t('optionsLater', { minutes: optionsAfterMinutes })}</p>
      <div className="flex flex-col gap-2">
        <ButtonLink href={rescheduleHref} variant={optionsOpen ? 'primary' : 'secondary'} full aria-describedby="late-free">
          {t('reschedule')}
        </ButtonLink>
        <p id="late-free" className="text-xs text-muted">
          {t('rescheduleFree')}
        </p>
        {optionsOpen ? (
          <ButtonLink href="/therapists" variant="secondary" full>
            {t('otherTherapist')}
          </ButtonLink>
        ) : null}
      </div>

      <Dialog open={breathing} onClose={() => setBreathing(false)} title={t('breathing')}>
        <BreathingPacer />
        <Button variant="ghost" full onClick={() => setBreathing(false)}>
          {t('close')}
        </Button>
      </Dialog>
    </section>
  );
}
