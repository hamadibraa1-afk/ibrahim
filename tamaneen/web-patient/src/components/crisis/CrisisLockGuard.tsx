'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from '@/i18n/navigation';

/** Screens that stay open while a crisis event holds the chat: the crisis screen, the contact number and the safety plan. */
export const OPEN_WHILE_LOCKED = ['/crisis', '/settings/safety', '/safety-plan'];

export function isOpenWhileLocked(pathname: string): boolean {
  return OPEN_WHILE_LOCKED.some((p) => pathname === p || pathname.startsWith(`${p}/`));
}

/**
 * While the chat is locked (P06: "the chat is paused; the professional will reopen it"), every other signed-in
 * screen sends the patient back to the crisis screen. The emergency numbers are always reachable from it.
 */
export function CrisisLockGuard({ locked }: { locked: boolean }) {
  const pathname = usePathname();
  const router = useRouter();
  const redirect = locked && !isOpenWhileLocked(pathname);

  useEffect(() => {
    if (redirect) router.replace('/crisis');
  }, [redirect, router]);

  return null;
}
