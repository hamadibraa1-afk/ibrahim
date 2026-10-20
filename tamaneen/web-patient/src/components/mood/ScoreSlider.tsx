'use client';

import { useId, type ReactNode } from 'react';

/**
 * A 0–10 slider. "big" shows the value large above the track with word labels at the ends (P30);
 * "scale" shows 0, the value and 10 under the track (P10). The input carries aria-valuetext so screen
 * readers hear words, not just a number. In Arabic the track runs from the right, like the page.
 */
export function ScoreSlider({
  label,
  value,
  onChange,
  valueText,
  variant,
  lowLabel,
  highLabel,
  disabled,
}: {
  label: ReactNode;
  value: number;
  onChange: (next: number) => void;
  valueText: string;
  variant: 'big' | 'scale';
  lowLabel?: ReactNode;
  highLabel?: ReactNode;
  disabled?: boolean;
}) {
  const id = useId();
  const input = (
    <input
      id={id}
      type="range"
      min={0}
      max={10}
      step={1}
      value={value}
      disabled={disabled}
      aria-valuetext={valueText}
      onChange={(e) => onChange(Number(e.target.value))}
      className="h-11 w-full cursor-pointer accent-primary disabled:cursor-not-allowed disabled:opacity-50"
    />
  );

  if (variant === 'big') {
    return (
      <div className="flex flex-col gap-2">
        <label htmlFor={id} className="text-base font-bold">
          {label}
        </label>
        <p aria-hidden="true" className="text-center text-5xl font-bold text-primary">
          {value}
        </p>
        {input}
        <div aria-hidden="true" className="flex justify-between text-sm text-muted">
          <span>{lowLabel}</span>
          <span>{highLabel}</span>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <label htmlFor={id} className="text-base font-bold">
        {label}
      </label>
      {input}
      <div aria-hidden="true" className="flex justify-between text-sm text-muted">
        <span>{0}</span>
        <span className="text-base font-bold text-primary">{value}</span>
        <span>{10}</span>
      </div>
    </div>
  );
}
