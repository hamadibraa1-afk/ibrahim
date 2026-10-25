'use client';

import type { ComponentProps, MouseEvent } from 'react';
import { useTranslations } from 'next-intl';
import { Link, useRouter } from '@/i18n/navigation';

/** True when the previous page in this tab belongs to the app, so history back stays inside it. */
export function cameFromThisApp(): boolean {
  try {
    return document.referrer !== '' && new URL(document.referrer).origin === window.location.origin && window.history.length > 1;
  } catch {
    return false;
  }
}

/**
 * "Back" that returns to the previous screen of this app when there is one (keeping what it had saved),
 * and otherwise follows `fallback`. Without JavaScript it is a plain link to the fallback.
 */
export function HistoryBackLink({ fallback }: { fallback: ComponentProps<typeof Link>['href'] }) {
  const t = useTranslations('ui');
  const router = useRouter();

  function onClick(e: MouseEvent<HTMLAnchorElement>) {
    if (cameFromThisApp()) {
      e.preventDefault();
      router.back();
    }
  }

  return (
    <Link href={fallback} onClick={onClick} className="inline-flex min-h-touch items-center gap-2 text-sm font-semibold text-primary">
      <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="rtl:rotate-180">
        <path d="M15 6l-6 6 6 6" />
      </svg>
      {t('back')}
    </Link>
  );
}
