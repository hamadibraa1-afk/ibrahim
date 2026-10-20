'use client';

import type { ReactNode } from 'react';

/** Two or three mutually exclusive choices shown as one control (contact type). */
export function Segmented<T extends string>({ value, onChange, options, labelledBy }: { value: T; onChange: (v: T) => void; options: { value: T; label: ReactNode }[]; labelledBy: string }) {
  return (
    <div role="radiogroup" aria-labelledby={labelledBy} className="flex overflow-hidden rounded-[14px] border-[1.5px] border-line bg-surface">
      {options.map((o) => {
        const selected = o.value === value;
        return (
          <button
            key={o.value}
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={() => onChange(o.value)}
            className={`min-h-[52px] flex-1 px-3 text-base font-semibold transition-colors ${selected ? 'bg-primary text-surface' : 'text-primary hover:bg-tint-teal'}`}
          >
            {o.label}
          </button>
        );
      })}
    </div>
  );
}
