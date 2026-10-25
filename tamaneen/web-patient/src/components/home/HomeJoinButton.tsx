'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { useRouter } from '@/i18n/navigation';

const POLL_FOR_LINK_MS = 30_000;
/** setTimeout cannot wait longer than this (about 24.8 days). */
const MAX_TIMEOUT = 2_147_000_000;

/**
 * PB-43: the "join session" button on the home card. Hidden until joinAvailableAt (ten minutes before the
 * start); at that moment the page refreshes to pick up joinUrl, which opens in a new tab.
 */
export function HomeJoinButton({ joinUrl, joinAvailableAt, name }: { joinUrl: string | null; joinAvailableAt: string; name: string }) {
  const t = useTranslations('home');
  const router = useRouter();
  const opensAt = Date.parse(joinAvailableAt);
  const [now, setNow] = useState(() => Date.now());
  const available = now >= opensAt;
  const url = joinUrl && /^https:\/\//i.test(joinUrl) ? joinUrl : null;

  useEffect(() => {
    if (available) return;
    const id = window.setTimeout(() => {
      setNow(Date.now());
      router.refresh();
    }, Math.min(Math.max(opensAt - Date.now(), 0) + 250, MAX_TIMEOUT));
    return () => window.clearTimeout(id);
  }, [available, opensAt, router]);

  // The link can lag a little behind the opening time; check back until it is there.
  useEffect(() => {
    if (!available || url) return;
    const id = window.setInterval(() => router.refresh(), POLL_FOR_LINK_MS);
    return () => window.clearInterval(id);
  }, [available, url, router]);

  if (!available || !url) return null;
  return (
    <a
      href={url}
      target="_blank"
      rel="noopener noreferrer"
      data-testid="home-join"
      aria-label={t('joinLabel', { name })}
      className="flex min-h-cta w-full items-center justify-center rounded-full bg-surface px-6 text-base font-semibold text-primary"
    >
      {t('join')}
    </a>
  );
}
