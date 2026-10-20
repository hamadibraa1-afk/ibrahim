import type { ReactNode } from 'react';
import { PhoneNumber } from './PhoneNumber';

const variants = {
  solid: 'bg-help text-surface',
  outline: 'border-2 border-help bg-surface text-help',
  chip: 'min-h-touch rounded-full bg-help/10 px-4 text-sm text-help',
};

/**
 * A plain tel: link rendered on the server, so emergency numbers work without JavaScript and from the
 * offline cache. The label and the number are both part of the link's accessible name.
 */
export function CallLink({ number, children, variant = 'solid', icon = false }: { number: string; children: ReactNode; variant?: keyof typeof variants; icon?: boolean }) {
  const big = variant !== 'chip';
  return (
    <a
      href={`tel:${number}`}
      className={`flex items-center justify-center gap-3 font-bold ${big ? 'min-h-[60px] w-full rounded-[18px] px-5 text-lg' : ''} ${variants[variant]}`}
    >
      {icon ? (
        <svg aria-hidden="true" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" className="shrink-0">
          <path d="M5 4h4l2 5-2.5 1.5a11 11 0 005 5L15 13l5 2v4a2 2 0 01-2 2A16 16 0 013 6a2 2 0 012-2" />
        </svg>
      ) : null}
      <span className="flex flex-wrap items-center justify-center gap-x-2">
        <span>{children}</span>
        <PhoneNumber number={number} />
      </span>
    </a>
  );
}
