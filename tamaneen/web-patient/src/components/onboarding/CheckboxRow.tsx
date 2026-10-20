'use client';

import { useId, type ReactNode } from 'react';

/** A large, labelled checkbox (consents, "I saved the codes", "remember this device"). */
export function CheckboxRow({
  checked,
  onChange,
  label,
  description,
  trailing,
  disabled,
  describedBy,
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  label: ReactNode;
  description?: ReactNode;
  trailing?: ReactNode;
  disabled?: boolean;
  describedBy?: string;
}) {
  const id = useId();
  return (
    <div className="flex items-start gap-3">
      <input
        id={id}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        onChange={(e) => onChange(e.target.checked)}
        aria-describedby={[description ? `${id}-desc` : null, describedBy].filter(Boolean).join(' ') || undefined}
        className="mt-0.5 h-6 w-6 shrink-0 cursor-pointer rounded-[6px] accent-[rgb(var(--primary))] disabled:cursor-not-allowed"
      />
      <div className="flex min-w-0 flex-1 flex-col gap-1">
        <label htmlFor={id} className="flex min-h-6 cursor-pointer flex-wrap items-center gap-2 text-base font-semibold">
          {label}
          {trailing}
        </label>
        {description ? (
          <p id={`${id}-desc`} className="text-sm leading-relaxed text-muted">
            {description}
          </p>
        ) : null}
      </div>
    </div>
  );
}
