import type { ReactNode } from 'react';

export function PageHeading({ title, subtitle, eyebrow }: { title: ReactNode; subtitle?: ReactNode; eyebrow?: ReactNode }) {
  return (
    <div className="flex flex-col gap-2 pt-2">
      {eyebrow ? <p className="text-sm font-semibold text-primary">{eyebrow}</p> : null}
      <h1 className="text-2xl font-bold leading-snug">{title}</h1>
      {subtitle ? <p className="text-base leading-relaxed text-muted">{subtitle}</p> : null}
    </div>
  );
}
