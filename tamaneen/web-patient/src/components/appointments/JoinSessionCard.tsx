'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { formatRelativeDays, formatTime } from '@/lib/format';
import { buttonClass } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { asLocale } from '@/components/matching/labels';

/** setTimeout cannot wait longer than this (about 24.8 days). */
const MAX_TIMEOUT = 2_147_000_000;
const POLL_FOR_LINK_MS = 30_000;

const dayKey = (ms: number, timeZone: string) =>
  new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date(ms));

/**
 * P28 "session link" card. The button stays disabled until joinAvailableAt (labelled "in 2 days" or with
 * the time on the day itself); then the page refreshes to pick up joinUrl, which opens in a new tab.
 */
export function JoinSessionCard({ joinUrl, joinAvailableAt, timeZone }: { joinUrl: string | null; joinAvailableAt: string; timeZone: string }) {
  const t = useTranslations('appointmentDetail');
  const locale = asLocale(useLocale());
  const router = useRouter();
  const [now, setNow] = useState(() => Date.now());
  const opensAt = Date.parse(joinAvailableAt);
  const available = now >= opensAt;
  const url = joinUrl && /^https:\/\//i.test(joinUrl) ? joinUrl : null;

  useEffect(() => {
    if (available) return;
    const id = window.setTimeout(() => {
      const current = Date.now();
      setNow(current);
      if (current >= opensAt) router.refresh();
    }, Math.min(Math.max(opensAt - Date.now(), 0) + 250, MAX_TIMEOUT));
    return () => window.clearTimeout(id);
  }, [available, opensAt, now, router]);

  // The link can lag a little behind the opening time; check back until it is there.
  useEffect(() => {
    if (!available || url) return;
    const id = window.setInterval(() => router.refresh(), POLL_FOR_LINK_MS);
    return () => window.clearInterval(id);
  }, [available, url, router]);

  let action;
  if (available && url) {
    action = (
      <a href={url} target="_blank" rel="noopener noreferrer" className={buttonClass('primary', true)}>
        {t('join')}
        <span className="sr-only">{t('newTab')}</span>
      </a>
    );
  } else {
    const when = available
      ? null
      : dayKey(opensAt, timeZone) === dayKey(now, timeZone)
        ? formatTime(joinAvailableAt, locale, timeZone)
        : formatRelativeDays(joinAvailableAt, locale, new Date(now), timeZone);
    action = (
      <button type="button" disabled className={buttonClass('primary', true)}>
        {when ? t('joinWhen', { when }) : t('join')}
      </button>
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
