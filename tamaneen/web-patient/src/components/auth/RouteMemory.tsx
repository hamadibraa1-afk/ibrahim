'use client';

import { useEffect } from 'react';
import { usePathname } from '@/i18n/navigation';
import { rememberRoute } from './returnTo';

/** Remembers the current signed-in screen of this tab, for the return after an expired session. */
export function RouteMemory({ pseudonymId }: { pseudonymId: string }) {
  const pathname = usePathname();
  useEffect(() => {
    rememberRoute(`${pathname}${window.location.search}`, pseudonymId);
  }, [pathname, pseudonymId]);
  return null;
}
