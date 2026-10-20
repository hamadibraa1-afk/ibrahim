'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';

/** Readable by page script on purpose (not HttpOnly): it only says whether to show the button. */
export const QUICK_EXIT_COOKIE = 'tamaneen_qe';
const CHANGED_EVENT = 'tamaneen:quick-exit';
const NEUTRAL_PAGE = 'https://www.google.com';

export function quickExitEnabled(): boolean {
  return typeof document !== 'undefined' && new RegExp(`(?:^|;\\s*)${QUICK_EXIT_COOKIE}=1(?:;|$)`).test(document.cookie);
}

/** Called when the notification preference is loaded or saved, so every signed-in page knows at once. */
export function setQuickExitCookie(on: boolean): void {
  const secure = window.location.protocol === 'https:' ? '; secure' : '';
  document.cookie = on
    ? `${QUICK_EXIT_COOKIE}=1; path=/; max-age=31536000; samesite=lax${secure}`
    : `${QUICK_EXIT_COOKIE}=; path=/; max-age=0; samesite=lax${secure}`;
  window.dispatchEvent(new Event(CHANGED_EVENT));
}

/** Leaves for an ordinary site and replaces this entry in the history, so Back doesn't land on the app. */
export function quickExit(): void {
  try {
    window.history.replaceState(null, '', '/');
  } catch {
    // Some browsers refuse history changes in private windows; leaving still works.
  }
  window.location.replace(NEUTRAL_PAGE);
}

/**
 * A small fixed "Quick exit" button on signed-in pages when the patient turned it on (P33). It sits above
 * the bottom navigation, on the end side.
 */
export function QuickExit() {
  const t = useTranslations('settings.quickExit');
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    const sync = () => setVisible(quickExitEnabled());
    sync();
    window.addEventListener(CHANGED_EVENT, sync);
    return () => window.removeEventListener(CHANGED_EVENT, sync);
  }, []);

  if (!visible) return null;
  return (
    <button
      type="button"
      onClick={quickExit}
      aria-label={t('aria')}
      data-testid="quick-exit"
      className="fixed bottom-24 end-4 z-40 inline-flex min-h-touch items-center gap-2 rounded-full bg-ink px-4 text-sm font-semibold text-surface shadow-lg"
    >
      <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round">
        <path d="M6 6l12 12M18 6L6 18" />
      </svg>
      {t('label')}
    </button>
  );
}
