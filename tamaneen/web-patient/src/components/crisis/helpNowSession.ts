'use client';

import { useEffect, useSyncExternalStore } from 'react';

/**
 * Whether the header's Help now acts for a signed-in patient. The header lives in the root layout, which must
 * not read cookies (that would make every public page dynamic), so the signed-in layout announces itself here
 * with the safety numbers it rendered. Before hydration, and on public pages, Help now stays a plain link to
 * the emergency resources (P14), which also works without JavaScript.
 */
export interface HelpNowSession {
  emergencyNumber: string | null;
  supportLine: string | null;
}

let current: HelpNowSession | null = null;
const listeners = new Set<() => void>();

function set(value: HelpNowSession | null) {
  current = value;
  listeners.forEach((l) => l());
}

function subscribe(listener: () => void) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function useHelpNowSession(): HelpNowSession | null {
  return useSyncExternalStore(
    subscribe,
    () => current,
    () => null,
  );
}

/** Rendered by the signed-in layout. */
export function HelpNowSessionMarker({ emergencyNumber, supportLine }: HelpNowSession) {
  useEffect(() => {
    set({ emergencyNumber, supportLine });
    return () => set(null);
  }, [emergencyNumber, supportLine]);
  return null;
}
