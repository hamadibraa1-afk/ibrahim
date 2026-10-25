import { api } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { onboardingStep } from '@/lib/onboarding';
import { takeReturnPath } from '@/lib/returnPath';
import { takeReturnRoute } from './returnTo';

/**
 * After the BFF has set the session cookies: where this patient should land. An unfinished onboarding
 * step (or a crisis lock) comes first; otherwise the last screen of this tab before the session ended
 * (P18, P35), then home. Only the route remembered for this same patient is used, so on a shared device
 * nobody is sent to the previous patient's screen; the path saved by the session-expired page (not tied
 * to a patient) is only consumed, so it cannot leak into a later sign-in.
 */
export async function signedInPath(): Promise<string> {
  const me = await api<Me>('me');
  const back = takeReturnRoute(me.pseudonymId);
  takeReturnPath();
  return onboardingStep(me) ?? back ?? '/home';
}
