'use client';

import { HELP_QUEUE_KEY } from '@/components/crisis/helpNow';
import { clearOutbox } from '@/components/pwa/outbox';
import { clearCachedPlan } from './planCache';

/**
 * Call before signing out (or after deleting the account): removes the encrypted safety plan and its device
 * key, any Help now request still waiting in the queue, and the outbox (P66, which can hold safety-plan edits),
 * so nothing is left for the next person on a shared device and a queued write is never sent from someone
 * else's session.
 */
export async function clearLocalSafetyData(): Promise<void> {
  try {
    window.localStorage.removeItem(HELP_QUEUE_KEY);
  } catch {
    // Nothing stored.
  }
  await clearOutbox();
  await clearCachedPlan();
}
