'use client';

import { HELP_QUEUE_KEY } from '@/components/crisis/helpNow';
import { clearCachedPlan } from './planCache';

/**
 * Call before signing out (or after deleting the account): removes the encrypted safety plan and its device
 * key, and any Help now request still waiting in the queue, so nothing is left for the next person on a
 * shared device and a queued request is never sent from someone else's session.
 */
export async function clearLocalSafetyData(): Promise<void> {
  try {
    window.localStorage.removeItem(HELP_QUEUE_KEY);
  } catch {
    // Nothing stored.
  }
  await clearCachedPlan();
}
