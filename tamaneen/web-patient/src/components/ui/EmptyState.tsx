import type { ReactNode } from 'react';

export function EmptyState({ title, body, action }: { title: ReactNode; body?: ReactNode; action?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-card bg-surface px-6 py-10 text-center">
      <p className="text-lg font-bold">{title}</p>
      {body ? <p className="text-sm text-muted">{body}</p> : null}
      {action}
    </div>
  );
}
