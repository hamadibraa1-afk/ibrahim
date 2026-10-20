'use client';

import { useEffect, useRef, type ReactNode } from 'react';

/**
 * Page title that takes focus when `focusKey` changes (a new question, the next step), or on mount with
 * `focusOnMount` (a view that replaces the form, such as a result), so screen-reader and keyboard users
 * start reading from the new content. Otherwise the first render keeps the browser's normal focus.
 */
export function StepHeading({
  title,
  subtitle,
  eyebrow,
  focusKey,
  focusOnMount,
  level = 1,
}: {
  title: ReactNode;
  subtitle?: ReactNode;
  eyebrow?: ReactNode;
  focusKey?: string | number;
  focusOnMount?: boolean;
  level?: 1 | 2;
}) {
  const ref = useRef<HTMLHeadingElement>(null);
  const first = useRef(true);

  useEffect(() => {
    if (first.current) {
      first.current = false;
      if (!focusOnMount) return;
    }
    ref.current?.focus();
    // Focus follows focusKey only; focusOnMount is read on the first run.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [focusKey]);

  const Tag = level === 1 ? 'h1' : 'h2';
  return (
    <div className="flex flex-col gap-2 pt-2">
      {eyebrow ? <p className="text-sm font-semibold text-primary">{eyebrow}</p> : null}
      <Tag ref={ref} tabIndex={-1} className="text-2xl font-bold leading-snug outline-none">
        {title}
      </Tag>
      {subtitle ? <p className="text-base leading-relaxed text-muted">{subtitle}</p> : null}
    </div>
  );
}
