'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Link } from '@/i18n/navigation';

/** setTimeout cannot wait longer than this (about 24.8 days). */
const MAX_TIMEOUT = 2_147_000_000;

/**
 * PB-43: the "join session" button on the home card. Hidden until joinAvailableAt (ten minutes before the
 * start); then it opens the session lobby (P41), where the camera check and the join happen.
 */
export function HomeJoinButton({ appointmentId, joinAvailableAt, name }: { appointmentId: string; joinAvailableAt: string; name: string }) {
  const t = useTranslations('home');
  const opensAt = Date.parse(joinAvailableAt);
  const [now, setNow] = useState(() => Date.now());
  const available = now >= opensAt;

  useEffect(() => {
    if (available) return;
    const id = window.setTimeout(() => setNow(Date.now()), Math.min(Math.max(opensAt - Date.now(), 0) + 250, MAX_TIMEOUT));
    return () => window.clearTimeout(id);
  }, [available, opensAt]);

  if (!available) return null;
  return (
    <Link
      href={`/appointments/${encodeURIComponent(appointmentId)}/lobby`}
      data-testid="home-join"
      aria-label={t('joinLabel', { name })}
      className="flex min-h-cta w-full items-center justify-center rounded-full bg-surface px-6 text-base font-semibold text-primary"
    >
      {t('join')}
    </Link>
  );
}
