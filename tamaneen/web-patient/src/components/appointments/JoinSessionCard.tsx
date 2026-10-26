'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { formatRelativeDays, formatTime } from '@/lib/format';
import { buttonClass, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { asLocale } from '@/components/matching/labels';

/** setTimeout cannot wait longer than this (about 24.8 days). */
const MAX_TIMEOUT = 2_147_000_000;

const dayKey = (ms: number, timeZone: string) =>
  new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(ms));

/**
 * P28 "session room" card (PB-43). Until joinAvailableAt (ten minutes before) the main button is disabled and says
 * when it opens ("in 2 days", or the time on the day itself); then it opens the lobby (P41), where the join happens.
 * The lobby is also reachable earlier to check the camera and write the pre-session note.
 */
export function JoinSessionCard({ appointmentId, joinAvailableAt, timeZone }: { appointmentId: string; joinAvailableAt: string; timeZone: string }) {
  const t = useTranslations('appointmentDetail');
  const locale = asLocale(useLocale());
  const [now, setNow] = useState(() => Date.now());
  const opensAt = Date.parse(joinAvailableAt);
  const available = now >= opensAt;
  const lobby = `/appointments/${encodeURIComponent(appointmentId)}/lobby`;

  useEffect(() => {
    if (available) return;
    const id = window.setTimeout(() => setNow(Date.now()), Math.min(Math.max(opensAt - Date.now(), 0) + 250, MAX_TIMEOUT));
    return () => window.clearTimeout(id);
  }, [available, opensAt, now]);

  let action;
  if (available) {
    action = (
      <ButtonLink href={lobby} full data-testid="join-lobby">
        {t('join')}
      </ButtonLink>
    );
  } else {
    const when = dayKey(opensAt, timeZone) === dayKey(now, timeZone) ? formatTime(joinAvailableAt, locale, timeZone) : formatRelativeDays(joinAvailableAt, locale, new Date(now), timeZone);
    action = (
      <>
        <button type="button" disabled className={buttonClass('primary', true)}>
          {t('joinWhen', { when })}
        </button>
        <ButtonLink href={lobby} variant="ghost" full>
          {t('prepare')}
        </ButtonLink>
      </>
    );
  }

  return (
    <Card aria-labelledby="join-heading" className="flex flex-col gap-3 border border-line">
      <h2 id="join-heading" className="text-lg font-bold">
        {t('linkTitle')}
      </h2>
      <p className="text-base leading-relaxed text-muted">{t('linkBody')}</p>
      {action}
    </Card>
  );
}
