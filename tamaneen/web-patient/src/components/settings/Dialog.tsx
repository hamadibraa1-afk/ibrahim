'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useId, useRef, type ReactNode } from 'react';
import { Button } from '@/components/ui/Button';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/**
 * A modal dialog: focus moves inside (to the element marked data-autofocus, or the first control), Tab
 * stays inside, Escape closes, and focus returns to the control that opened it.
 */
export function Dialog({ open, onClose, title, children, tone = 'default' }: { open: boolean; onClose: () => void; title: ReactNode; children: ReactNode; tone?: 'default' | 'danger' }) {
  const titleId = useId();
  const panel = useRef<HTMLDivElement>(null);
  const close = useRef(onClose);
  close.current = onClose;

  useEffect(() => {
    if (!open) return;
    const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const node = panel.current;
    const focusables = () => Array.from(node?.querySelectorAll<HTMLElement>(FOCUSABLE) ?? []);
    (node?.querySelector<HTMLElement>('[data-autofocus]') ?? focusables()[0] ?? node)?.focus();

    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault();
        close.current();
        return;
      }
      if (event.key !== 'Tab') return;
      const items = focusables();
      const first = items[0];
      const last = items[items.length - 1];
      if (!first || !last) return;
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    document.addEventListener('keydown', onKeyDown);
    const overflow = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.removeEventListener('keydown', onKeyDown);
      document.body.style.overflow = overflow;
      previous?.focus();
    };
  }, [open]);

  if (!open) return null;
  return (
    <div className="fixed inset-0 z-50 flex items-end justify-center bg-ink/40 p-4 sm:items-center" onMouseDown={(e) => e.target === e.currentTarget && onClose()}>
      <div ref={panel} role="dialog" aria-modal="true" aria-labelledby={titleId} tabIndex={-1} className="flex w-full max-w-md flex-col gap-4 rounded-card bg-surface p-5 shadow-xl">
        <h2 id={titleId} className={`text-lg font-bold ${tone === 'danger' ? 'text-help' : ''}`}>
          {title}
        </h2>
        {children}
      </div>
    </div>
  );
}

/** The dialog's "Cancel"; it takes the initial focus so a stray Enter never confirms a destructive action. */
export function DialogCancel({ onClick }: { onClick: () => void }) {
  const t = useTranslations('settings.dialog');
  return (
    <Button variant="secondary" full onClick={onClick} data-autofocus>
      {t('cancel')}
    </Button>
  );
}
