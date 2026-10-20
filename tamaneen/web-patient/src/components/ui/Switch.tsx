'use client';

import { useId, type ReactNode } from 'react';

/** A labelled on/off switch (role="switch"). The whole row is clickable. */
export function Switch({ checked, onChange, label, description, disabled }: { checked: boolean; onChange: (next: boolean) => void; label: ReactNode; description?: ReactNode; disabled?: boolean }) {
  const id = useId();
  return (
    <div className="flex items-center justify-between gap-4 py-2">
      <div className="flex flex-col">
        <span id={`${id}-label`} className="text-base font-semibold">
          {label}
        </span>
        {description ? (
          <span id={`${id}-desc`} className="text-sm text-muted">
            {description}
          </span>
        ) : null}
      </div>
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-labelledby={`${id}-label`}
        aria-describedby={description ? `${id}-desc` : undefined}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className={`relative inline-flex h-8 w-14 shrink-0 items-center rounded-full transition-colors disabled:opacity-50 ${checked ? 'bg-primary' : 'bg-line'}`}
      >
        <span aria-hidden="true" className={`absolute h-6 w-6 rounded-full bg-surface shadow transition-all ${checked ? 'start-7' : 'start-1'}`} />
      </button>
    </div>
  );
}
