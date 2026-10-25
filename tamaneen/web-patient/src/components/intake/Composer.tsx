'use client';

import { useTranslations } from 'next-intl';
import { useId, type KeyboardEvent } from 'react';

/** Same limit as the API (EC-19). Longer text is kept in the box, not cut, and the patient is asked to send it in parts. */
export const MAX_MESSAGE_LENGTH = 2000;
const COUNTER_FROM = 1800;

/** Message box: Enter sends, Shift+Enter adds a line, IME composition (Arabic keyboards) never sends early. */
export function Composer({ value, onChange, onSend, disabled }: { value: string; onChange: (text: string) => void; onSend: () => void; disabled: boolean }) {
  const t = useTranslations('intake');
  const id = useId();
  const showCounter = value.length >= COUNTER_FROM;
  const tooLong = value.length > MAX_MESSAGE_LENGTH;
  const blocked = disabled || tooLong;

  function onKeyDown(e: KeyboardEvent<HTMLTextAreaElement>) {
    if (e.key === 'Enter' && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault();
      if (!blocked) onSend();
    }
  }

  return (
    <form
      className="flex flex-col gap-1"
      onSubmit={(e) => {
        e.preventDefault();
        if (!blocked) onSend();
      }}
    >
      <label htmlFor={id} className="sr-only">
        {t('composerLabel')}
      </label>
      <div className="flex items-end gap-2">
        <textarea
          id={id}
          rows={1}
          value={value}
          placeholder={t('placeholder')}
          aria-invalid={tooLong || undefined}
          aria-describedby={`${id}-hint${showCounter ? ` ${id}-count` : ''}${tooLong ? ` ${id}-too-long` : ''}`}
          onChange={(e) => onChange(e.target.value)}
          onKeyDown={onKeyDown}
          dir="auto"
          className="max-h-40 min-h-[52px] w-full resize-none rounded-[18px] border border-line bg-surface px-4 py-3 text-base leading-relaxed text-ink placeholder:text-muted/70 focus:border-primary"
        />
        <button
          type="submit"
          aria-label={t('send')}
          disabled={blocked || value.trim() === ''}
          className="flex h-[52px] w-[52px] shrink-0 items-center justify-center rounded-[18px] bg-primary text-surface disabled:opacity-50"
        >
          <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round" className="rtl:rotate-180">
            <path d="M5 12h14M13 6l6 6-6 6" />
          </svg>
        </button>
      </div>
      <p id={`${id}-hint`} className="sr-only">
        {t('composerHint')}
      </p>
      <p id={`${id}-count`} aria-live="polite" className={`text-xs tabular-nums ${tooLong ? 'font-semibold text-help' : 'text-muted'} ${showCounter ? '' : 'sr-only'}`}>
        {showCounter ? t('counter', { count: value.length, max: MAX_MESSAGE_LENGTH }) : null}
      </p>
      {tooLong ? (
        <p id={`${id}-too-long`} role="alert" className="text-sm font-semibold text-help">
          {t('tooLong', { max: MAX_MESSAGE_LENGTH })}
        </p>
      ) : null}
    </form>
  );
}
