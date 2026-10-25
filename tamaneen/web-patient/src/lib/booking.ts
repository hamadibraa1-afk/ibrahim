import type { Me } from './api/types';

/**
 * Mirrors the server's booking rule (SchedulingEndpoints.EnsureCanBookAsync): a reviewed file on the intake path,
 * or a finished safety check on the direct path, and no crisis lock. A booked patient's triage is `matched`, so
 * they stay eligible. P25 hides «اختيار موعد» when this is false (PB-39).
 */
export function canBook(me: Me): boolean {
  if (me.chatLocked) return false;
  const o = me.onboarding;
  if (o.triageStatus === 'reviewed' || o.triageStatus === 'matched') return true;
  return o.path === 'direct' && o.screeningDone;
}
