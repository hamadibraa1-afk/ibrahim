'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { formatDateTime } from '@/lib/format';
import { Dialog } from '@/components/booking/Dialog';
import { Button } from '@/components/ui/Button';
import { useOnline } from './hooks';
import { RetryIcon } from './OutboxStatus';
import { discardOutboxItem, flushOutbox, retryOutboxItem, useOutbox, type OutboxItem } from './outbox';

function detail(item: OutboxItem, t: ReturnType<typeof useTranslations<'systemStates.outbox'>>): { line: string | null; note: string | null } {
  const body = (item.body ?? {}) as { score?: number; status?: string; note?: string };
  if (item.kind === 'mood') return { line: typeof body.score === 'number' ? t('moodDetail', { score: body.score }) : null, note: body.note ?? null };
  if (item.kind === 'homework-checkin') {
    const status = body.status === 'done' || body.status === 'partial' || body.status === 'skipped' ? t(`checkinStatus.${body.status}`) : null;
    return { line: [item.label, status].filter(Boolean).join(' · ') || null, note: body.note ?? null };
  }
  return { line: t('planDetail'), note: null };
}

/**
 * P66 outbox page: every write waiting on this phone, what it will send, and its state. ◷ waiting items go out
 * on their own; ⟳ sends them now. An item the server refused shows its reason, with send again and discard
 * (after a confirmation: a queued item is never deleted silently).
 */
export function OutboxView() {
  const t = useTranslations('systemStates.outbox');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const online = useOnline();
  const items = useOutbox();
  const [sending, setSending] = useState(false);
  const [discarding, setDiscarding] = useState<OutboxItem | null>(null);

  async function retryAll() {
    setSending(true);
    try {
      await flushOutbox({ force: true });
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center justify-between gap-3">
        <p className="leading-relaxed text-muted">{online ? t('intro') : t('weak')}</p>
        <Button variant="secondary" compact onClick={() => void retryAll()} busy={sending} aria-label={t('retryNow')}>
          <RetryIcon />
        </Button>
      </div>

      {items.length === 0 ? (
        <p className="rounded-card bg-surface p-4 leading-relaxed" data-testid="outbox-empty">
          {t('empty')}
        </p>
      ) : (
        <ul className="flex flex-col gap-3" aria-label={t('title')}>
          {items.map((item) => {
            const { line, note } = detail(item, t);
            return (
              <li key={item.id} data-testid="outbox-item" className="flex flex-col gap-2 rounded-card bg-surface p-4 shadow-[0_1px_0_rgb(var(--line))]">
                <div className="flex flex-wrap items-baseline justify-between gap-2">
                  <h2 className="font-bold">{t(`kinds.${item.kind}`)}</h2>
                  <span className="text-xs text-muted">{t('queuedAt', { time: formatDateTime(item.createdAt, locale) })}</span>
                </div>
                {line ? <p dir="auto">{line}</p> : null}
                {note ? (
                  <p dir="auto" className="whitespace-pre-line rounded-[14px] bg-bg px-3 py-2 text-sm leading-relaxed">
                    {note}
                  </p>
                ) : null}
                {item.state === 'waiting' ? (
                  <p className="text-sm font-semibold text-muted">{t('waiting')}</p>
                ) : (
                  <div className="flex flex-col gap-2" role="alert">
                    <p className="text-sm font-semibold text-help">
                      {t('failed')} · <span dir="auto">{item.error ?? t('failedGeneric')}</span>
                    </p>
                    <div className="flex flex-wrap gap-2">
                      <Button variant="secondary" compact onClick={() => void retryOutboxItem(item.id)}>
                        {t('retry')}
                      </Button>
                      <Button variant="ghost" compact onClick={() => setDiscarding(item)}>
                        {t('discard')}
                      </Button>
                    </div>
                  </div>
                )}
              </li>
            );
          })}
        </ul>
      )}

      <Dialog open={discarding !== null} onClose={() => setDiscarding(null)} title={t('discard')} description={t('discardConfirm')}>
        {/* Cancel first: it gets the focus when the dialog opens. */}
        <div className="flex flex-wrap gap-3">
          <Button variant="secondary" onClick={() => setDiscarding(null)}>
            {t('cancel')}
          </Button>
          <Button
            variant="danger"
            onClick={() => {
              if (discarding) void discardOutboxItem(discarding.id);
              setDiscarding(null);
            }}
          >
            {t('discardYes')}
          </Button>
        </div>
      </Dialog>
    </div>
  );
}
