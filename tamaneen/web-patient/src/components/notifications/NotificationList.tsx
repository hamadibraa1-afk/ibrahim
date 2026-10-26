'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import type { Inbox, InboxItem, NotificationCategory } from '@/lib/api/types';
import { apiMessage } from '@/components/pwa/errors';
import { Button } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { Notice } from '@/components/ui/Notice';
import { relativeTime } from './relative';

const ICONS: Record<NotificationCategory, string> = {
  messages: 'M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z',
  appointments: 'M8 2v4M16 2v4M3 10h18M5 4h14a2 2 0 012 2v14a2 2 0 01-2 2H5a2 2 0 01-2-2V6a2 2 0 012-2z',
  homework: 'M9 11l3 3L22 4M21 12v7a2 2 0 01-2 2H5a2 2 0 01-2-2V5a2 2 0 012-2h11',
  care: 'M20.8 4.6a5.5 5.5 0 00-7.8 0L12 5.7l-1-1.1a5.5 5.5 0 00-7.8 7.8l1 1.1L12 21l7.8-7.5 1-1.1a5.5 5.5 0 000-7.8z',
  risk: 'M12 9v4M12 17h.01M10.3 3.9L1.8 18a2 2 0 001.7 3h17a2 2 0 001.7-3L13.7 3.9a2 2 0 00-3.4 0z',
  security: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z',
  system: 'M12 22a10 10 0 100-20 10 10 0 000 20zM12 16v-4M12 8h.01',
};

/**
 * P46 notification centre: each entry has an icon, its text, a relative time and an unread dot. Opening one marks
 * it read and goes to the screen it is about. A new sign-in carries «ليس أنت؟ أنهِ الجلسة», which ends that session
 * after a confirmation and suggests changing the password (P32).
 */
export function NotificationList({ initial }: { initial: Inbox }) {
  const t = useTranslations('notifications');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const router = useRouter();
  const [items, setItems] = useState<InboxItem[]>(initial.items);
  const [cursor, setCursor] = useState(initial.nextCursor);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState<InboxItem | null>(null);
  const [ended, setEnded] = useState(false);

  async function open(item: InboxItem) {
    setItems((list) => list.map((i) => (i.id === item.id ? { ...i, read: true } : i)));
    try {
      if (!item.read) await api(`me/inbox/${item.id}/read`, { method: 'POST' });
    } catch {
      // Opening still works; the entry is marked read next time.
    }
    if (item.link) router.push(item.link);
  }

  async function more() {
    if (!cursor) return;
    setLoading(true);
    setError(null);
    try {
      const next = await api<Inbox>(`me/inbox?cursor=${encodeURIComponent(cursor)}`);
      setItems((list) => [...list, ...next.items]);
      setCursor(next.nextCursor);
    } catch (e) {
      setError(apiMessage(e) ?? t('error'));
    } finally {
      setLoading(false);
    }
  }

  async function endSession(item: InboxItem) {
    setError(null);
    try {
      await api(`me/sessions/${encodeURIComponent(item.data.sessionId ?? '')}`, { method: 'DELETE' });
      await api(`me/inbox/${item.id}/read`, { method: 'POST' }).catch(() => undefined);
      setConfirming(null);
      setEnded(true);
    } catch (e) {
      setConfirming(null);
      setError(apiMessage(e) ?? t('error'));
    }
  }

  if (items.length === 0) return <EmptyState title={t('empty')} body={t('emptyBody')} />;

  return (
    <div className="flex flex-col gap-3">
      {ended ? (
        <Notice tone="success">
          {t('sessionEnded')}{' '}
          <button type="button" className="font-semibold underline underline-offset-4" onClick={() => router.push('/settings/security')}>
            {t('changePassword')}
          </button>
        </Notice>
      ) : null}
      {error ? <Notice tone="danger">{error}</Notice> : null}
      <ul className="flex flex-col gap-2" aria-label={t('title')}>
        {items.map((item) => (
          <li key={item.id} className={`flex items-start gap-3 rounded-card p-4 ${item.read ? 'bg-surface' : 'bg-tint-teal'}`} data-testid="notification">
            <span aria-hidden="true" className={`mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-full ${item.category === 'risk' || item.category === 'security' ? 'bg-tint-sand text-help' : 'bg-bg text-primary'}`}>
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d={ICONS[item.category] ?? ICONS.system} />
              </svg>
            </span>
            <div className="flex min-w-0 flex-1 flex-col gap-1">
              <button type="button" onClick={() => open(item)} className="min-h-touch text-start font-semibold leading-relaxed text-ink">
                {item.text}
                {!item.read ? <span className="sr-only"> · {t('unread')}</span> : null}
              </button>
              <span className="text-sm text-muted">
                {relativeTime(item.createdAt, locale)}
                {item.kind === 'security.new_sign_in' && item.data.sessionId ? (
                  <>
                    {' · '}
                    <button type="button" className="font-semibold text-help underline underline-offset-4" onClick={() => setConfirming(item)}>
                      {t('notYou')}
                    </button>
                  </>
                ) : null}
              </span>
            </div>
            {!item.read ? <span aria-hidden="true" className="mt-2 h-2.5 w-2.5 shrink-0 rounded-full bg-primary" data-testid="unread-dot" /> : null}
          </li>
        ))}
      </ul>
      {confirming ? (
        <div role="alertdialog" aria-labelledby="end-session-title" className="flex flex-col gap-3 rounded-card border-[1.5px] border-help bg-surface p-4">
          <p id="end-session-title" className="font-bold">{t('confirmTitle')}</p>
          <p className="text-sm text-muted">{t('confirmBody')}</p>
          <div className="flex flex-wrap gap-2">
            <Button variant="danger" onClick={() => endSession(confirming)}>{t('confirmEnd')}</Button>
            <Button variant="ghost" onClick={() => setConfirming(null)}>{t('cancel')}</Button>
          </div>
        </div>
      ) : null}
      {cursor ? (
        <Button variant="secondary" busy={loading} disabled={loading} onClick={more}>
          {t('more')}
        </Button>
      ) : null}
    </div>
  );
}
