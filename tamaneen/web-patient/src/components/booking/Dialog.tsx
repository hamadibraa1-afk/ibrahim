'use client';

import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react';
import { createPortal } from 'react-dom';

const FOCUSABLE = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: ReactNode;
  description?: ReactNode;
  children?: ReactNode;
  /**
   * Gets focus on open (the element itself, or the first focusable element inside it); default is the
   * first focusable element of the dialog. Point it at the least destructive action.
   */
  initialFocus?: RefObject<HTMLElement | null>;
  /** While true, Escape and the backdrop do not close the dialog (e.g. a request is in flight). */
  locked?: boolean;
}

/**
 * Accessible modal dialog: role="dialog" + aria-modal, labelled by its title and described by its text.
 * Focus moves inside on open, Tab and Shift+Tab stay inside, Escape closes, and focus returns to the
 * element that opened it. The rest of the page is made inert while it is open.
 */
export function Dialog({ open, onClose, title, description, children, initialFocus, locked = false }: DialogProps) {
  const id = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const onCloseRef = useRef(onClose);
  const lockedRef = useRef(locked);
  onCloseRef.current = onClose;
  lockedRef.current = locked;

  useEffect(() => {
    if (!open) return;
    const opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const panel = panelRef.current;
    const siblings = Array.from(document.body.children).filter((el): el is HTMLElement => el instanceof HTMLElement && !el.contains(panel));
    const wasInert = siblings.map((el) => el.inert);
    siblings.forEach((el) => {
      el.inert = true;
    });

    const preferred = initialFocus?.current;
    const target =
      (preferred && (preferred.matches(FOCUSABLE) ? preferred : preferred.querySelector<HTMLElement>(FOCUSABLE))) ??
      panel?.querySelector<HTMLElement>(FOCUSABLE) ??
      panel;
    target?.focus();

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        event.preventDefault();
        if (!lockedRef.current) onCloseRef.current();
        return;
      }
      if (event.key !== 'Tab' || !panel) return;
      const items = Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE));
      if (items.length === 0) {
        event.preventDefault();
        panel.focus();
        return;
      }
      const first = items[0]!;
      const last = items[items.length - 1]!;
      const active = document.activeElement;
      if (event.shiftKey && (active === first || !panel.contains(active))) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && (active === last || !panel.contains(active))) {
        event.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKeyDown);

    return () => {
      document.removeEventListener('keydown', onKeyDown);
      siblings.forEach((el, i) => {
        el.inert = wasInert[i] ?? false;
      });
      opener?.focus();
    };
  }, [open, initialFocus]);

  if (!open || typeof document === 'undefined') return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center p-4 sm:items-center">
      <div aria-hidden="true" className="absolute inset-0 bg-ink/50" onClick={() => !locked && onClose()} />
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={`${id}-title`}
        aria-describedby={description ? `${id}-desc` : undefined}
        tabIndex={-1}
        className="relative flex w-full max-w-md flex-col gap-4 rounded-card bg-surface p-5 shadow-xl outline-none"
      >
        <h2 id={`${id}-title`} className="text-xl font-bold">
          {title}
        </h2>
        {description ? (
          <div id={`${id}-desc`} className="text-base leading-relaxed text-muted">
            {description}
          </div>
        ) : null}
        {children}
      </div>
    </div>,
    document.body,
  );
}
