import { api } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { onboardingStep } from '@/lib/onboarding';
import { takeReturnRoute } from './returnTo';

/**
 * After the BFF has set the session cookies: where this patient should land. An unfinished onboarding
 * step (or a crisis lock) comes first; otherwise the last screen of this tab before the session ended,
 * then home.
 */
export async function signedInPath(): Promise<string> {
  const me = await api<Me>('me');
  const back = takeReturnRoute(me.pseudonymId);
  return onboardingStep(me) ?? back ?? '/home';
}
