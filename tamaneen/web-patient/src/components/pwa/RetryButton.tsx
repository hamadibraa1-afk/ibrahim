'use client';

import { buttonClass } from '@/components/ui/Button';

/** Reloads the page the person was trying to open (the offline page is served in its place). */
export function RetryButton({ label }: { label: string }) {
  return (
    <button type="button" className={buttonClass('primary', true)} onClick={() => window.location.reload()}>
      {label}
    </button>
  );
}
