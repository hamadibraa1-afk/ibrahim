import type { ReactNode } from 'react';

const tones = {
  info: 'bg-tint-teal text-primary-strong',
  success: 'bg-tint-teal text-primary-strong',
  warning: 'bg-tint-sand text-ink',
  danger: 'border-[1.5px] border-help bg-surface text-help',
};

/** Inline message. Danger notices are announced (role="alert"), the rest politely (role="status"). */
export function Notice({ tone = 'info', children, className = '' }: { tone?: keyof typeof tones; children: ReactNode; className?: string }) {
  return (
    <div role={tone === 'danger' ? 'alert' : 'status'} className={`rounded-[16px] px-4 py-3 text-sm leading-relaxed ${tones[tone]} ${className}`}>
      {children}
    </div>
  );
}
