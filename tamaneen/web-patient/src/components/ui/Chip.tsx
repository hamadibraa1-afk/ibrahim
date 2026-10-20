'use client';

import type { ButtonHTMLAttributes } from 'react';

/** A toggle chip for filters and tags (aria-pressed). */
export function Chip({ selected, className = '', ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { selected: boolean }) {
  return (
    <button
      type="button"
      aria-pressed={selected}
      className={`inline-flex min-h-touch items-center rounded-full border px-4 text-sm font-semibold ${selected ? 'border-primary bg-primary text-surface' : 'border-line bg-surface text-ink'} ${className}`}
      {...rest}
    />
  );
}

/** A static label chip (specialties, statuses). */
export function Tag({ children, tone = 'teal' }: { children: React.ReactNode; tone?: 'teal' | 'sand' | 'help' | 'muted' }) {
  const tones = { teal: 'bg-tint-teal text-primary-strong', sand: 'bg-tint-sand text-ink', help: 'bg-help/10 text-help', muted: 'bg-line text-muted' };
  return <span className={`inline-flex items-center rounded-full px-3 py-1 text-xs font-semibold ${tones[tone]}`}>{children}</span>;
}
