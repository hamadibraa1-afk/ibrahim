import type { ReactNode } from 'react';

export interface DetailRow {
  label: ReactNode;
  value: ReactNode;
  valueClassName?: string;
}

/** Label/value rows in a card (P25 session details, P28 appointment). A description list for screen readers. */
export function DetailRows({ rows, label }: { rows: DetailRow[]; label?: string }) {
  return (
    <section aria-label={label} className="rounded-card border border-line bg-surface px-4">
      <dl className="divide-y divide-line">
        {rows.map((row, i) => (
          <div key={i} className="flex items-center justify-between gap-4 py-4">
            <dt className="text-sm text-muted">{row.label}</dt>
            <dd className={`text-end text-base font-bold ${row.valueClassName ?? ''}`}>{row.value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
