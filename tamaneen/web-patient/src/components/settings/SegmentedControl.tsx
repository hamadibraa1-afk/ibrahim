'use client';

import { useId, useRef, type KeyboardEvent, type ReactNode } from 'react';

export interface SegmentOption<T extends string> {
  value: T;
  label: ReactNode;
  lang?: string;
}

/**
 * A row of mutually exclusive choices (role="radiogroup"). One stop in the Tab order; the arrow keys move
 * the choice, following the reading direction, as native radio buttons do.
 */
export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
  disabled,
  invalid,
  describedBy,
  labelClassName = 'text-base font-bold',
}: {
  label: ReactNode;
  options: SegmentOption<T>[];
  value: T | null;
  onChange: (next: T) => void;
  disabled?: boolean;
  invalid?: boolean;
  describedBy?: string;
  labelClassName?: string;
}) {
  const id = useId();
  const refs = useRef<(HTMLButtonElement | null)[]>([]);
  const selectedIndex = options.findIndex((o) => o.value === value);
  const tabStop = selectedIndex >= 0 ? selectedIndex : 0;

  function onKeyDown(event: KeyboardEvent<HTMLButtonElement>, index: number) {
    const rtl = event.currentTarget.closest('[dir]')?.getAttribute('dir') === 'rtl';
    const forward = rtl ? 'ArrowLeft' : 'ArrowRight';
    const back = rtl ? 'ArrowRight' : 'ArrowLeft';
    let next: number | null = null;
    if (event.key === forward || event.key === 'ArrowDown') next = (index + 1) % options.length;
    else if (event.key === back || event.key === 'ArrowUp') next = (index - 1 + options.length) % options.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = options.length - 1;
    if (next === null) return;
    event.preventDefault();
    const option = options[next];
    if (!option) return;
    onChange(option.value);
    refs.current[next]?.focus();
  }

  return (
    <div className="flex flex-col gap-2">
      <p id={`${id}-label`} className={labelClassName}>
        {label}
      </p>
      <div role="radiogroup" aria-labelledby={`${id}-label`} aria-describedby={describedBy} aria-invalid={invalid || undefined} aria-disabled={disabled || undefined} className="flex gap-2">
        {options.map((option, index) => {
          const checked = option.value === value;
          return (
            <button
              key={option.value}
              ref={(el) => {
                refs.current[index] = el;
              }}
              type="button"
              role="radio"
              aria-checked={checked}
              tabIndex={index === tabStop ? 0 : -1}
              disabled={disabled}
              lang={option.lang}
              onClick={() => onChange(option.value)}
              onKeyDown={(e) => onKeyDown(e, index)}
              className={`flex min-h-[48px] flex-1 items-center justify-center rounded-[14px] px-3 text-center text-sm font-semibold transition-colors disabled:opacity-50 ${
                checked ? 'border-2 border-primary bg-tint-teal text-primary-strong' : `border ${invalid ? 'border-help' : 'border-line'} bg-surface text-ink`
              }`}
            >
              {option.label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
