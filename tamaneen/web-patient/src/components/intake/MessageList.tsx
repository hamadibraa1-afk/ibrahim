'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef } from 'react';
import type { ChatMessage } from '@/lib/api/types';
import { useReducedMotion } from '@/components/pwa/hooks';

export interface DisplayMessage extends ChatMessage {
  /** An optimistic patient bubble that could not be sent yet (offline); it goes out when the connection returns. */
  queued?: boolean;
  /** The server could not take it (not a network problem); it can be retried with the same client id. */
  failed?: boolean;
  /** Sent with the message and reused on every retry, so the API stores it once (PB-21, PB-27). */
  clientMessageId?: string;
}

/**
 * The conversation log. Assistant bubbles sit at the start side, the patient's at the end side. New
 * messages are announced politely and scrolled into view (instantly when reduced motion is requested).
 */
export function MessageList({
  messages,
  typing,
  scrollKey,
  onRetry,
  retrying = false,
}: {
  messages: DisplayMessage[];
  typing: boolean;
  scrollKey: string;
  onRetry?: (message: DisplayMessage) => void;
  retrying?: boolean;
}) {
  const t = useTranslations('intake');
  const end = useRef<HTMLDivElement>(null);
  const reduced = useReducedMotion();

  useEffect(() => {
    end.current?.scrollIntoView?.({ behavior: reduced ? 'auto' : 'smooth', block: 'end' });
  }, [scrollKey, typing, reduced]);

  return (
    <div className="flex flex-col">
      <ol role="log" aria-live="polite" aria-label={t('logLabel')} className="flex flex-col gap-3" data-testid="intake-log">
        {messages.map((m) => {
          const mine = m.role === 'patient';
          return (
            <li key={m.id} className={`flex ${mine ? 'justify-end' : 'justify-start'}`}>
              <div
                className={
                  mine
                    ? `max-w-[85%] rounded-[20px] rounded-ee-md bg-primary px-4 py-3 text-surface ${m.queued || m.failed ? 'opacity-70' : ''}`
                    : 'max-w-[85%] rounded-[20px] rounded-es-md border border-line bg-surface px-4 py-3 text-ink'
                }
                data-role={m.role}
              >
                <span className="sr-only">{mine ? t('patientName') : t('assistantName')}</span>
                <p dir="auto" className="whitespace-pre-line break-words leading-relaxed">
                  {m.text}
                </p>
                {m.fallback ? <p className="mt-1 text-xs font-medium text-muted">{t('fallbackLabel')}</p> : null}
                {m.queued ? <p className="mt-1 text-xs font-medium text-surface/90">{t('queued')}</p> : null}
                {m.failed ? <p className="mt-1 text-xs font-medium text-surface/90">{t('notSent')}</p> : null}
                {(m.queued || m.failed) && onRetry ? (
                  <button
                    type="button"
                    disabled={retrying}
                    onClick={() => onRetry(m)}
                    className="mt-2 inline-flex min-h-touch items-center rounded-full bg-surface px-4 text-sm font-semibold text-primary disabled:opacity-50"
                  >
                    {t('retrySend')}
                  </button>
                ) : null}
              </div>
            </li>
          );
        })}
        {typing ? (
          <li className="flex justify-start" data-testid="typing">
            <div className="flex items-center gap-1.5 rounded-[20px] rounded-es-md border border-line bg-surface px-4 py-4">
              <span className="sr-only">{t('typing')}</span>
              {[0, 1, 2].map((i) => (
                <span key={i} aria-hidden="true" className="h-2 w-2 animate-pulse rounded-full bg-muted" style={{ animationDelay: `${i * 200}ms` }} />
              ))}
            </div>
          </li>
        ) : null}
      </ol>
      <div ref={end} aria-hidden="true" />
    </div>
  );
}
