'use client';

import type { ButtonHTMLAttributes, ReactNode } from 'react';

/** A large answer choice (questionnaires, yes/no). Use inside role="radiogroup". */
export function OptionButton({ selected, trailing, className = '', children, ...rest }: ButtonHTMLAttributes<HTMLButtonElement> & { selected?: boolean; trailing?: ReactNode }) {
  return (
    <button
      type="button"
      role="radio"
      aria-checked={Boolean(selected)}
      className={`flex min-h-[48px] w-full items-center justify-between gap-3 rounded-[12px] border px-4 text-start text-base font-medium ${selected ? 'border-primary bg-tint-teal' : 'border-line bg-surface'} ${className}`}
      {...rest}
    >
      <span>{children}</span>
      {trailing !== undefined ? <span className="text-sm text-muted">{trailing}</span> : null}
    </button>
  );
}
