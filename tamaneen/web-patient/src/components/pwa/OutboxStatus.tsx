'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { Link, usePathname, useRouter } from '@/i18n/navigation';
import { isCrisis } from './errors';
import { useOnline } from './hooks';
import { OUTBOX_SENT_EVENT, flushOutbox, nextAttemptAt, setOutboxOwner, useOutbox, type OutboxSentDetail } from './outbox';

/** Shortest wait between timer runs, so a clock jump never turns into a tight loop. */
const MIN_TIMER_MS = 2_000;

export function RetryIcon({ spinning = false }: { spinning?: boolean }) {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" className={spinning ? 'motion-safe:animate-spin' : ''}>
      <path d="M20 12a8 8 0 11-2.3-5.6M20 4v5h-5" />
    </svg>
  );
}

/**
 * P66, the global part. Rendered by the signed-in layout: it names whose outbox this is, sends it when the
 * connection returns, on the retry timer and when the app comes back to the foreground, and opens the crisis
 * screen at once when a queued text turns out to be a crisis at the gate. The banner says what is true now:
 * the connection is weak, how many items are waiting on the phone, and how many were refused, with a link to
 * the outbox page and ⟳ to send now.
 */
export function OutboxStatus({ owner }: { owner: string }) {
  const t = useTranslations('systemStates.outbox');
  const router = useRouter();
  const pathname = usePathname();
  const online = useOnline();
  const items = useOutbox();
  const [sending, setSending] = useState(false);
  const routerRef = useRef(router);
  routerRef.current = router;

  useEffect(() => {
    setOutboxOwner(owner);
    void flushOutbox();
    return () => setOutboxOwner(null);
  }, [owner]);

  // Back online: send everything now. Back in the foreground: send what is due.
  useEffect(() => {
    if (online) void flushOutbox({ force: true });
  }, [online]);
  useEffect(() => {
    const visible = () => {
      if (document.visibilityState === 'visible') void flushOutbox();
    };
    document.addEventListener('visibilitychange', visible);
    return () => document.removeEventListener('visibilitychange', visible);
  }, []);

  // The retry timer (0, 2, 10, 30 s): wake up when the next item is due. Items changing re-arms it.
  useEffect(() => {
    const due = nextAttemptAt();
    if (due === null) return;
    const id = window.setTimeout(() => void flushOutbox(), Math.max(MIN_TIMER_MS, due - Date.now()));
    return () => window.clearTimeout(id);
  }, [items]);

  // A queued note the gate found to be a crisis: the crisis screen opens the moment it arrives.
  useEffect(() => {
    const sent = (event: Event) => {
      const detail = (event as CustomEvent<OutboxSentDetail>).detail;
      if (isCrisis(detail?.response)) routerRef.current.replace('/crisis');
    };
    window.addEventListener(OUTBOX_SENT_EVENT, sent);
    return () => window.removeEventListener(OUTBOX_SENT_EVENT, sent);
  }, []);

  const waiting = items.filter((i) => i.state === 'waiting').length;
  const failed = items.length - waiting;
  if (online && waiting === 0 && failed === 0) return null;

  async function retryNow() {
    setSending(true);
    try {
      await flushOutbox({ force: true });
    } finally {
      setSending(false);
    }
  }

  const onOutboxPage = pathname === '/outbox';

  return (
    <div role="status" data-testid="outbox-banner" className="mb-3 flex flex-col gap-1 rounded-card border border-line bg-tint-sand px-4 py-2.5 text-sm">
      <div className="flex items-center justify-between gap-3">
        <p className="font-semibold">{online ? t('waitingCount', { count: waiting }) : t('weak')}</p>
        <button
          type="button"
          onClick={() => void retryNow()}
          disabled={sending}
          aria-label={t('retryNow')}
          className="inline-flex h-11 w-11 shrink-0 items-center justify-center rounded-full border-[1.5px] border-line bg-surface text-primary disabled:opacity-60"
        >
          <RetryIcon spinning={sending} />
        </button>
      </div>
      {!online && waiting > 0 ? <p className="text-muted">{t('waitingCount', { count: waiting })}</p> : null}
      {failed > 0 ? <p className="font-semibold text-help">{t('failedCount', { count: failed })}</p> : null}
      {(waiting > 0 || failed > 0) && !onOutboxPage ? (
        <Link href="/outbox" className="inline-flex min-h-touch items-center self-start font-semibold text-primary underline">
          {t('view')}
        </Link>
      ) : null}
    </div>
  );
}
