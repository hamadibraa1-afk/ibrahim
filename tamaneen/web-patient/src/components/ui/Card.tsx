import type { HTMLAttributes } from 'react';

export function Card({ className = '', tone = 'surface', ...rest }: HTMLAttributes<HTMLElement> & { tone?: 'surface' | 'teal' | 'sand' | 'help' }) {
  const tones = { surface: 'bg-surface', teal: 'bg-tint-teal', sand: 'bg-tint-sand', help: 'bg-surface border-[1.5px] border-help' };
  return <section className={`rounded-card p-4 shadow-[0_1px_0_rgb(var(--line))] ${tones[tone]} ${className}`} {...rest} />;
}
