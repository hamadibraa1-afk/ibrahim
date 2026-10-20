import type { ReactNode } from 'react';

const icons = {
  offline: { tone: 'bg-line text-muted', path: 'M2 8.5a15 15 0 0120 0M5 12a10 10 0 0114 0M8.5 15.5a5 5 0 017 0M12 19h.01M3 3l18 18' },
  clock: { tone: 'bg-tint-teal text-primary', path: 'M12 21a9 9 0 100-18 9 9 0 000 18zM12 7v5l3 2' },
  wrench: { tone: 'bg-tint-sand text-accent', path: 'M14.7 6.3a4 4 0 00-5.4 5.4L3 18l3 3 6.3-6.3a4 4 0 005.4-5.4l-2.5 2.5-2.5-.5-.5-2.5z' },
};

/** The centered layout of the P35 system states: an icon, a title, a short body and actions. */
export function StatePanel({ icon, title, body, children }: { icon: keyof typeof icons; title: ReactNode; body: ReactNode; children?: ReactNode }) {
  const { tone, path } = icons[icon];
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 pt-16 text-center">
      <span aria-hidden="true" className={`flex h-20 w-20 items-center justify-center rounded-full ${tone}`}>
        <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d={path} />
        </svg>
      </span>
      <h1 className="text-2xl font-bold">{title}</h1>
      <p className="leading-relaxed text-muted">{body}</p>
      <div className="flex w-full flex-col items-stretch gap-3">{children}</div>
    </div>
  );
}
