'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { Link, usePathname } from '@/i18n/navigation';
import { useHelpNowSession } from '@/components/crisis/helpNowSession';
import { api } from '@/lib/api/client';
import type { UnreadCount } from '@/lib/api/types';

/**
 * The header bell (P46 entry). Shown only for a signed-in patient (the signed-in layout announces itself, as for
 * Help now). The unread count is read again on every page change; a failure just hides the badge.
 */
export function NotificationBell() {
  const t = useTranslations('notifications');
  const session = useHelpNowSession();
  const pathname = usePathname();
  const [count, setCount] = useState(0);

  useEffect(() => {
    if (!session) return;
    let live = true;
    api<UnreadCount>('me/inbox/unread-count', { navigate: false })
      .then((r) => live && setCount(r.unreadCount))
      .catch(() => live && setCount(0));
    return () => {
      live = false;
    };
  }, [session, pathname]);

  if (!session) return null;
  const label = count > 0 ? t('bellUnread', { count }) : t('bell');
  return (
    <Link href="/notifications" aria-label={label} data-testid="notification-bell" className="relative inline-flex h-11 w-11 items-center justify-center rounded-full text-ink hover:bg-surface">
      <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
        <path d="M18 8a6 6 0 10-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 01-3.4 0" />
      </svg>
      {count > 0 ? (
        <span aria-hidden="true" className="absolute end-1 top-1 min-w-[18px] rounded-full bg-help px-1 text-center text-[11px] font-bold leading-[18px] text-surface">
          {count > 9 ? '9+' : count}
        </span>
      ) : null}
    </Link>
  );
}
