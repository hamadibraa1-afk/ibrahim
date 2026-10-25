import { api } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { landingPath } from '@/lib/onboarding';
import { takeReturnPath } from '@/lib/returnPath';

/**
 * After the BFF has set the session cookies: where this patient should land. Unfinished onboarding and a
 * crisis lock come first; otherwise the page the patient was on when the session expired (P35), if any.
 */
export async function signedInPath(): Promise<string> {
  const me = await api<Me>('me');
  const landing = landingPath(me);
  const back = takeReturnPath();
  return landing === '/home' && back ? back : landing;
}
